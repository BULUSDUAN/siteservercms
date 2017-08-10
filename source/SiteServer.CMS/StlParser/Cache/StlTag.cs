using SiteServer.CMS.Core;
using SiteServer.CMS.Model;

namespace SiteServer.CMS.StlParser.Cache
{
    public class StlTag
    {
        public static StlTagInfo GetStlTagInfo(int publishmentSystemId, string tagName, string guid)
        {
            var cacheKey = StlCacheUtils.GetCacheKeyByGuid(guid, nameof(StlTag), nameof(GetStlTagInfo), publishmentSystemId.ToString(), tagName);
            var retval = StlCacheUtils.GetCache<StlTagInfo>(cacheKey);
            if (retval != null) return retval;

            retval = DataProvider.StlTagDao.GetStlTagInfo(publishmentSystemId, tagName);
            StlCacheUtils.SetCache(cacheKey, retval);
            return retval;
        }
    }
}