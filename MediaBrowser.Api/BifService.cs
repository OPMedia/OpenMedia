using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using System;
using System.IO;

namespace MediaBrowser.Api
{
    /// <summary>
    /// BIF (Roku thumbnail archive) endpoints.
    /// Placeholder: BIF generation requires ffmpeg frame extraction which is not
    /// implemented yet; the endpoint returns 404 so clients fall back gracefully.
    /// </summary>
    [Route("/Videos/{Id}/index.bif", "GET", Summary = "Gets a BIF thumbnail archive")]
    [Authenticated]
    public class GetBifFile
    {
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public Guid Id { get; set; }

        [ApiMember(Name = "Width", Description = "The thumbnail width", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int? Width { get; set; }
    }

    [Authenticated]
    public class BifService : BaseApiService
    {
        public object Get(GetBifFile request)
        {
            // BIF generation (ffmpeg interval frame extraction + archive packaging) is not
            // implemented in this migration phase. 404 lets clients skip progress previews.
            throw new FileNotFoundException("BIF thumbnails are not supported");
        }
    }
}
