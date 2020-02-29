﻿using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using SS.CMS.Abstractions.Dto.Request;

namespace SS.CMS.Web.Controllers.Admin.Cms.Contents
{
    public partial class ContentsLayerImportController
    {
        public class GetResult
        {
            public int Value { get; set; }
            public List<KeyValuePair<int, string>> CheckedLevels { get; set; }
        }

        public class UploadRequest : ChannelRequest
        {
            public IFormFile File { get; set; }
        }

        public class UploadResult
        {
            public string Name { get; set; }
            public string Url { get; set; }
        }

        public class SubmitRequest : ChannelRequest
        {
            public string ImportType { get; set; }
            public int CheckedLevel { get; set; }
            public bool IsOverride { get; set; }
            public List<string> FileNames { get; set; }
        }
    }
}