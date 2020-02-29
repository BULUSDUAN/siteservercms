﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SS.CMS.Abstractions;
using SS.CMS.Abstractions.Parse;
using SS.CMS.StlParser.StlElement;
using SS.CMS.StlParser.Utility;
using SS.CMS.Core;

namespace SS.CMS.Services
{
    public partial class CreateManager
    {
        public async Task ExecuteAsync(int siteId, CreateType createType, int channelId, int contentId,
            int fileTemplateId, int specialId)
        {
            if (createType == CreateType.Channel)
            {
                await ExecuteChannelAsync(siteId, channelId);
            }
            else if (createType == CreateType.Content)
            {
                var site = await _siteRepository.GetAsync(siteId);
                var channelInfo = await _channelRepository.GetAsync(channelId);
                await ExecuteContentAsync(site, channelInfo, contentId);
            }
            else if (createType == CreateType.AllContent)
            {
                await ExecuteContentsAsync(siteId, channelId);
            }
            else if (createType == CreateType.File)
            {
                await ExecuteFileAsync(siteId, fileTemplateId);
            }
            else if (createType == CreateType.Special)
            {
                await ExecuteSpecialAsync(siteId, specialId);
            }
        }

        private async Task ExecuteContentsAsync(int siteId, int channelId)
        {
            var site = await _siteRepository.GetAsync(siteId);
            var channelInfo = await _channelRepository.GetAsync(channelId);

            var contentIdList = await _contentRepository.GetContentIdsCheckedAsync(site, channelInfo);

            foreach (var contentId in contentIdList)
            {
                await ExecuteContentAsync(site, channelInfo, contentId);
            }
        }

        private async Task ExecuteChannelAsync(int siteId, int channelId)
        {
            var site = await _siteRepository.GetAsync(siteId);
            var channelInfo = await _channelRepository.GetAsync(channelId);

            var count = await _contentRepository.GetCountAsync(site, channelInfo);
            if (!_channelRepository.IsCreatable(site, channelInfo, count)) return;

            var templateInfo = channelId == siteId
                ? await _templateRepository.GetIndexPageTemplateAsync(siteId)
                : await _templateRepository.GetChannelTemplateAsync(siteId, channelInfo);
            var filePath = await _pathManager.GetChannelPageFilePathAsync(site, channelId, 0);

            var config = await _configRepository.GetAsync();
            var pageInfo = ParsePage.GetPageInfo(_pathManager, config, channelId, 0, site, templateInfo, new Dictionary<string, object>());
            var contextInfo = new ParseContext(pageInfo)
            {
                ContextType = ParseType.Channel
            };
            var contentBuilder = new StringBuilder(await _pathManager.GetTemplateContentAsync(site, templateInfo));

            var stlLabelList = StlParserUtility.GetStlLabelList(contentBuilder.ToString());

            //如果标签中存在<stl:channel type="PageContent"></stl:channel>
            if (StlParserUtility.IsStlChannelElementWithTypePageContent(stlLabelList)) //内容存在
            {
                var stlElement = StlParserUtility.GetStlChannelElementWithTypePageContent(stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);
                contentBuilder.Replace(stlElement, stlElementTranslated);

                var innerBuilder = new StringBuilder(stlElement);
                await _parseManager.ParseInnerContentAsync(innerBuilder);
                var pageContentHtml = innerBuilder.ToString();
                var pageCount = StringUtils.GetCount(Constants.PagePlaceHolder, pageContentHtml) + 1; //一共需要的页数

                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();

                    var index = pageContentHtml.IndexOf(Constants.PagePlaceHolder, StringComparison.Ordinal);
                    var length = index == -1 ? pageContentHtml.Length : index;

                    var pageHtml = pageContentHtml.Substring(0, length);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));
                    await _parseManager.ReplacePageElementsInChannelPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        thePageInfo.PageChannelId, currentPageIndex, pageCount, 0);

                    filePath = await _pathManager.GetChannelPageFilePathAsync(site, thePageInfo.PageChannelId,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);

                    if (index != -1)
                    {
                        pageContentHtml = pageContentHtml.Substring(length + Constants.PagePlaceHolder.Length);
                    }
                }
            }
            //如果标签中存在<stl:pageContents>
            else if (StlParserUtility.IsStlElementExists(StlPageContents.ElementName, stlLabelList))
            {
                var stlElement = StlParserUtility.GetStlElement(StlPageContents.ElementName, stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);

                var pageContentsElementParser = await StlPageContents.GetAsync(stlElement, _parseManager);
                var (pageCount, totalNum) = pageContentsElementParser.GetPageCount();

                await pageInfo.AddPageBodyCodeIfNotExistsAsync(ParsePage.Const.Jquery);
                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();
                    var pageHtml = await pageContentsElementParser.ParseAsync(totalNum, currentPageIndex, pageCount, true);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                    await _parseManager.ReplacePageElementsInChannelPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        thePageInfo.PageChannelId, currentPageIndex, pageCount, totalNum);

                    filePath = await _pathManager.GetChannelPageFilePathAsync(site, thePageInfo.PageChannelId,
                        currentPageIndex);
                    thePageInfo.AddLastPageScript(pageInfo);

                    await GenerateFileAsync(filePath, pagedBuilder);

                    thePageInfo.ClearLastPageScript(pageInfo);
                    pageInfo.ClearLastPageScript();
                }
            }
            //如果标签中存在<stl:pageChannels>
            else if (StlParserUtility.IsStlElementExists(StlPageChannels.ElementName, stlLabelList))
            {
                var stlElement = StlParserUtility.GetStlElement(StlPageChannels.ElementName, stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);

                var pageChannelsElementParser = await StlPageChannels.GetAsync(stlElement, _parseManager);
                var pageCount = pageChannelsElementParser.GetPageCount(out var totalNum);

                await pageInfo.AddPageBodyCodeIfNotExistsAsync(ParsePage.Const.Jquery);
                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();
                    var pageHtml = await pageChannelsElementParser.ParseAsync(currentPageIndex, pageCount);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                    await _parseManager.ReplacePageElementsInChannelPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        thePageInfo.PageChannelId, currentPageIndex, pageCount, totalNum);

                    filePath = await _pathManager.GetChannelPageFilePathAsync(site, thePageInfo.PageChannelId,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);
                }
            }
            //如果标签中存在<stl:pageSqlContents>
            else if (StlParserUtility.IsStlElementExists(StlPageSqlContents.ElementName, stlLabelList))
            {
                var stlElement = StlParserUtility.GetStlElement(StlPageSqlContents.ElementName, stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);

                var pageSqlContentsElementParser = await StlPageSqlContents.GetAsync(stlElement, _parseManager);
                var pageCount = pageSqlContentsElementParser.GetPageCount(out var totalNum);

                await pageInfo.AddPageBodyCodeIfNotExistsAsync(ParsePage.Const.Jquery);
                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();
                    var pageHtml = await pageSqlContentsElementParser.ParseAsync(totalNum, currentPageIndex, pageCount, true);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                    await _parseManager.ReplacePageElementsInChannelPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        thePageInfo.PageChannelId, currentPageIndex, pageCount, totalNum);

                    filePath = await _pathManager.GetChannelPageFilePathAsync(site, thePageInfo.PageChannelId,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);
                }
            }
            else
            {
                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);
                await GenerateFileAsync(filePath, contentBuilder);
            }
        }

        private async Task ExecuteContentAsync(Site site, Channel channel, int contentId)
        {
            var contentInfo = await _contentRepository.GetAsync(site, channel, contentId);

            if (contentInfo == null)
            {
                return;
            }

            if (!contentInfo.Checked)
            {
                await DeleteContentAsync(site, channel.Id, contentId);
                return;
            }

            if (!ContentUtility.IsCreatable(channel, contentInfo)) return;

            if (site.IsCreateStaticContentByAddDate &&
                contentInfo.AddDate < site.CreateStaticContentAddDate)
            {
                return;
            }

            var templateInfo = await _templateRepository.GetContentTemplateAsync(site.Id, channel);
            var config = await _configRepository.GetAsync();
            var pageInfo = ParsePage.GetPageInfo(_pathManager, config, channel.Id, contentId, site, templateInfo,
                new Dictionary<string, object>());
            var contextInfo = new ParseContext(pageInfo)
            {
                ContextType = ParseType.Content
            };
            contextInfo.SetContent(contentInfo);
            var filePath = await _pathManager.GetContentPageFilePathAsync(site, pageInfo.PageChannelId, contentInfo, 0);
            var contentBuilder = new StringBuilder(await _pathManager.GetTemplateContentAsync(site, templateInfo));

            var stlLabelList = StlParserUtility.GetStlLabelList(contentBuilder.ToString());

            //如果标签中存在<stl:content type="PageContent"></stl:content>
            if (StlParserUtility.IsStlContentElementWithTypePageContent(stlLabelList)) //内容存在
            {
                var stlElement = StlParserUtility.GetStlContentElementWithTypePageContent(stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);
                contentBuilder.Replace(stlElement, stlElementTranslated);

                var innerBuilder = new StringBuilder(stlElement);
                await _parseManager.ParseInnerContentAsync(innerBuilder);
                var pageContentHtml = innerBuilder.ToString();
                var pageCount = StringUtils.GetCount(Constants.PagePlaceHolder, pageContentHtml) + 1; //一共需要的页数

                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();

                    var index = pageContentHtml.IndexOf(Constants.PagePlaceHolder, StringComparison.Ordinal);
                    var length = index == -1 ? pageContentHtml.Length : index;

                    var pageHtml = pageContentHtml.Substring(0, length);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));
                    await _parseManager.ReplacePageElementsInContentPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        channel.Id, contentId, currentPageIndex, pageCount);

                    filePath = await _pathManager.GetContentPageFilePathAsync(site, thePageInfo.PageChannelId, contentInfo,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);

                    if (index != -1)
                    {
                        pageContentHtml = pageContentHtml.Substring(length + Constants.PagePlaceHolder.Length);
                    }
                }
            }
            //如果标签中存在<stl:channel type="PageContent"></stl:channel>
            else if (StlParserUtility.IsStlChannelElementWithTypePageContent(stlLabelList)) //内容存在
            {
                var stlElement = StlParserUtility.GetStlChannelElementWithTypePageContent(stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);
                contentBuilder.Replace(stlElement, stlElementTranslated);

                var innerBuilder = new StringBuilder(stlElement);
                await _parseManager.ParseInnerContentAsync(innerBuilder);
                var pageContentHtml = innerBuilder.ToString();
                var pageCount = StringUtils.GetCount(Constants.PagePlaceHolder, pageContentHtml) + 1; //一共需要的页数

                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();

                    var index = pageContentHtml.IndexOf(Constants.PagePlaceHolder, StringComparison.Ordinal);
                    var length = index == -1 ? pageContentHtml.Length : index;

                    var pageHtml = pageContentHtml.Substring(0, length);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));
                    await _parseManager.ReplacePageElementsInContentPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        channel.Id, contentId, currentPageIndex, pageCount);

                    filePath = await _pathManager.GetContentPageFilePathAsync(site, thePageInfo.PageChannelId, contentInfo,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);

                    if (index != -1)
                    {
                        pageContentHtml = pageContentHtml.Substring(length + Constants.PagePlaceHolder.Length);
                    }
                }
            }
            //如果标签中存在<stl:pageContents>
            else if (StlParserUtility.IsStlElementExists(StlPageContents.ElementName, stlLabelList))
            {
                var stlElement = StlParserUtility.GetStlElement(StlPageContents.ElementName, stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);

                var pageContentsElementParser = await StlPageContents.GetAsync(stlElement, _parseManager);
                var (pageCount, totalNum) = pageContentsElementParser.GetPageCount();

                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();
                    var pageHtml = await pageContentsElementParser.ParseAsync(totalNum, currentPageIndex, pageCount, true);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                    await _parseManager.ReplacePageElementsInContentPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        channel.Id, contentId, currentPageIndex, pageCount);

                    filePath = await _pathManager.GetContentPageFilePathAsync(site, thePageInfo.PageChannelId, contentInfo,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);
                }
            }
            //如果标签中存在<stl:pageChannels>
            else if (StlParserUtility.IsStlElementExists(StlPageChannels.ElementName, stlLabelList))
            {
                var stlElement = StlParserUtility.GetStlElement(StlPageChannels.ElementName, stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);

                var pageChannelsElementParser = await StlPageChannels.GetAsync(stlElement, _parseManager);
                var pageCount = pageChannelsElementParser.GetPageCount(out _);

                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();
                    var pageHtml = await pageChannelsElementParser.ParseAsync(currentPageIndex, pageCount);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                    await _parseManager.ReplacePageElementsInContentPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        channel.Id, contentId, currentPageIndex, pageCount);

                    filePath = await _pathManager.GetContentPageFilePathAsync(site, thePageInfo.PageChannelId, contentInfo,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);
                }
            }
            //如果标签中存在<stl:pageSqlContents>
            else if (StlParserUtility.IsStlElementExists(StlPageSqlContents.ElementName, stlLabelList))
            {
                var stlElement = StlParserUtility.GetStlElement(StlPageSqlContents.ElementName, stlLabelList);
                var stlElementTranslated = _parseManager.StlEncrypt(stlElement);

                var pageSqlContentsElementParser = await StlPageSqlContents.GetAsync(stlElement, _parseManager);
                var pageCount = pageSqlContentsElementParser.GetPageCount(out var totalNum);

                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);

                for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                {
                    var thePageInfo = pageInfo.Clone();
                    var pageHtml = await pageSqlContentsElementParser.ParseAsync(totalNum, currentPageIndex, pageCount, true);
                    var pagedBuilder =
                        new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                    await _parseManager.ReplacePageElementsInContentPageAsync(pagedBuilder, thePageInfo, stlLabelList,
                        channel.Id, contentId, currentPageIndex, pageCount);

                    filePath = await _pathManager.GetContentPageFilePathAsync(site, thePageInfo.PageChannelId, contentInfo,
                        currentPageIndex);
                    await GenerateFileAsync(filePath, pagedBuilder);
                }
            }
            else //无翻页
            {
                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);
                await GenerateFileAsync(filePath, contentBuilder);
            }
        }

        private async Task ExecuteFileAsync(int siteId, int fileTemplateId)
        {
            var site = await _siteRepository.GetAsync(siteId);
            var templateInfo = await _templateRepository.GetAsync(fileTemplateId);
            if (templateInfo == null || templateInfo.TemplateType != TemplateType.FileTemplate)
            {
                return;
            }

            var config = await _configRepository.GetAsync();
            var pageInfo = ParsePage.GetPageInfo(_pathManager, config, siteId, 0, site, templateInfo, new Dictionary<string, object>());
            var contextInfo = new ParseContext(pageInfo);
            var filePath = await _pathManager.MapPathAsync(site, templateInfo.CreatedFileFullName);

            var contentBuilder = new StringBuilder(await _pathManager.GetTemplateContentAsync(site, templateInfo));
            await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);
            await GenerateFileAsync(filePath, contentBuilder);
        }

        private async Task ExecuteSpecialAsync(int siteId, int specialId)
        {
            var site = await _siteRepository.GetAsync(siteId);
            var templateInfoList = await _pathManager.GetSpecialTemplateListAsync(site, specialId);

            var config = await _configRepository.GetAsync();

            foreach (var templateInfo in templateInfoList)
            {
                var pageInfo = ParsePage.GetPageInfo(_pathManager, config, siteId, 0, site, templateInfo, new Dictionary<string, object>());
                var contextInfo = new ParseContext(pageInfo);
                var filePath = await _pathManager.MapPathAsync(site, templateInfo.CreatedFileFullName);

                var contentBuilder = new StringBuilder(templateInfo.Content);
                await _parseManager.ParseAsync(pageInfo, contextInfo, contentBuilder, filePath, false);
                await GenerateFileAsync(filePath, contentBuilder);
            }
        }

        private async Task GenerateFileAsync(string filePath, StringBuilder contentBuilder)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                await FileUtils.WriteTextAsync(filePath, contentBuilder.ToString());
            }
            catch
            {
                FileUtils.RemoveReadOnlyAndHiddenIfExists(filePath);
                await FileUtils.WriteTextAsync(filePath, contentBuilder.ToString());
            }
        }
    }
}