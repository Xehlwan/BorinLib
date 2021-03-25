using System.Collections.Generic;

namespace BorinLib.Web
{
    public interface IWebApiRequest
    {
        public IDictionary<string,string> Parameters { get; }
    }
}