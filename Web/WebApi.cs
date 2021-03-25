using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BorinLib.Web
{
    public class WebApi<T>
    {
        private static HttpClient httpClient = new();
        private readonly string fragment;
        private readonly string originalTemplate;
        private readonly List<QueryParam> queryParams;
        private readonly List<PathSegment> segments;
        /// <summary>
        /// Create a <see cref="WebApi{T}"/> from a template string in the format of: <c>http://www.contoso.com/api/{segmentVariable}/get?id={idDisplay}#fragment</c>.
        /// </summary>
        /// <param name="template">A template string used to create the structure of this <see cref="WebApi{T}"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown if template string is null.</exception>
        /// <exception cref="ArgumentException">Thrown if template string doesn't match templating rules.</exception>
        public WebApi(string template)
        {
            if (template is null)
                throw new ArgumentNullException(nameof(template));
            originalTemplate = template;

            string pathTemplate;
            string queryTemplate;
            (pathTemplate, queryTemplate, fragment) = SplitTemplate(originalTemplate);
            segments = ParsePath(pathTemplate);
            queryParams = ParseQuery(queryTemplate);
            if (!CheckUniqueness())
                throw new ArgumentException("Template string includes duplicate variable names.");
        }

        
        public async Task<T> GetFromJsonAsync(IWebApiRequest request, JsonSerializerOptions options = null)
        {
            string uri = BuildRequestUri(request);

            HttpResponseMessage response = await httpClient.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }
            string content = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<T>(content, options);
        }

        private string BuildRequestUri(IWebApiRequest request)
        {
            StringBuilder sb = new();
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.IsStatic)
                {
                    sb.Append(segment.SegmentString);
                }
                else if (request.Parameters.TryGetValue(segment.SegmentString, out string value))
                {
                    sb.Append(value);
                }
            }

            sb.Append('?');
            for (int i = 0; i < queryParams.Count; i++)
            {
                var queryParam = queryParams[i];
                if (request.Parameters.TryGetValue(queryParam.DisplayName, out string value))
                {
                    if (i > 0)
                    {
                        sb.Append('&');
                    }
                    sb.Append(queryParam.UriName);
                    sb.Append('=');
                    sb.Append(value);
                }
            }

            if (!string.IsNullOrEmpty(fragment))
            {
                sb.Append('#');
                sb.Append(fragment);
            }

            string uri = new(sb.ToString());
            return uri;
        }

        private bool CheckUniqueness()
        {
            HashSet<string> names = new();
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (!segment.IsStatic)
                {
                    if (!names.Add(segment.SegmentString))
                        return false;
                }
            }
            for (int i = 0; i < queryParams.Count; i++)
            {
                var param = queryParams[i];
                if (!names.Add(param.DisplayName))
                    return false;
            }
            return true;
        }

        private List<PathSegment> ParsePath(string pathTemplate)
        {
            List<PathSegment> segments = new();
            if (string.IsNullOrWhiteSpace(pathTemplate))
                return segments;
            ReadOnlySpan<char> template = pathTemplate;

            while (template.Length > 0)
            {
                // Find first delimiter.
                int variableStart = template.IndexOf('{');
                if (variableStart == -1)
                {
                    PathSegment segment = new(template, true);
                    segments.Add(segment);
                    break;
                }
                else
                {
                    PathSegment segment = new(template.Slice(0, variableStart), true);
                    template = template.Slice(variableStart + 1);

                    // Find second delimiter.
                    int variableEnd = template.IndexOf('}');
                    if (variableEnd == -1)
                    {
                        throw new ArgumentException("Mismatched braces in URI template string.");
                    }
                    else
                    {
                        segment = new(template.Slice(0, variableEnd), false);
                        segments.Add(segment);
                        template = template.Slice(variableEnd + 1);
                    }
                }
            }

            return segments;
        }

        private List<QueryParam> ParseQuery(string queryTemplate)
        {
            List<QueryParam> queryParams = new();
            if (string.IsNullOrWhiteSpace(queryTemplate))
                return queryParams;
            ReadOnlySpan<char> template = queryTemplate;

            while (template.Length >= 3)
            {
                // Pull out query parameter.
                int paramEnd = template.IndexOf('&');
                if (paramEnd == -1)
                {
                    QueryParam param = ParseQueryParam(template);
                    queryParams.Add(param);
                    break;
                }
                else
                {
                    QueryParam param = ParseQueryParam(template.Slice(0, paramEnd));
                    template = template.Slice(paramEnd + 1);
                }
            }

            return queryParams;
        }

        private QueryParam ParseQueryParam(ReadOnlySpan<char> template)
        {
            int equalsPosition = template.IndexOf('=');
            if (equalsPosition == -1)
                throw new ArgumentException("Missing equals-sign in query.");

            var uriName = template.Slice(0, equalsPosition);
            var displayName = template.Slice(equalsPosition + 1);

            ReadOnlySpan<char> trimChars = new[] { '{', '}', ' ' };
            uriName.Trim(trimChars);
            displayName.Trim(trimChars);
            if (uriName.Length == 0 || displayName.Length == 0)
                throw new ArgumentException("Missing name or identifier in query.");

            return new(uriName, displayName);
        }
        private (string pathTemplate, string queryTemplate, string fragment) SplitTemplate(ReadOnlySpan<char> template)
        {
            string pathTemplate;
            string queryTemplate;
            string fragment;

            // Ignore leading slash.
            if (template.StartsWith("/", StringComparison.Ordinal))
            {
                template = template.Slice(1);
            }

            // Pull out fragment.
            int fragmentStart = template.IndexOf('#');
            if (fragmentStart == -1)
            {
                fragment = "";
            }
            else
            {
                fragment = template.Slice(fragmentStart + 1).ToString();
                template = template.Slice(0, fragmentStart);
            }

            // Pull out path and query.
            int queryStart = template.IndexOf('?');
            if (queryStart == -1)
            {
                queryTemplate = "";
                pathTemplate = template.ToString();
            }
            else
            {
                queryTemplate = template.Slice(queryStart + 1).ToString();
                pathTemplate = template.Slice(0, queryStart).ToString();
            }

            return (pathTemplate, queryTemplate, fragment);
        }

        private class PathSegment
        {
            public bool IsStatic { get; }
            public string SegmentString { get; }

            public PathSegment(ReadOnlySpan<char> segment, bool isStatic) : this(segment.ToString(), isStatic)
            {
            }

            public PathSegment(string segment, bool isStatic)
            {
                IsStatic = isStatic;
                SegmentString = segment;
            }
        }

        private class QueryParam
        {
            public string DisplayName { get; }
            public string UriName { get; }
            public QueryParam(string uri, string display)
            {
                UriName = uri;
                DisplayName = display;
            }

            public QueryParam(ReadOnlySpan<char> uri, ReadOnlySpan<char> display) : this(uri.ToString(), display.ToString())
            {
            }
        }
    }
}