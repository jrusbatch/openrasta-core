﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenRasta.Configuration.MetaModel;
using OpenRasta.Web;
using HttpMethod = System.Net.Http.HttpMethod;

namespace OpenRasta.Plugins.ReverseProxy
{
  public class ReverseProxy
  {
    readonly ReverseProxyOptions _options;
    Lazy<HttpClient> _httpClient;

    public ReverseProxy(ReverseProxyOptions options, IMetaModelRepository modelRepository)
    {
      _options = options;
      _httpClient = new Lazy<HttpClient>(() => new HttpClient(_options.HttpMessageHandler())
          {
              Timeout = options.Timeout
          },
          LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<HttpResponseMessage> Send(ICommunicationContext context, string target)
    {
      var proxyTargetUri = GetProxyTargetUri(context.Request.Uri, context.PipelineData.SelectedResource.Results.Single(), target);
      var request = new HttpRequestMessage
      {
          Method = new HttpMethod(context.Request.HttpMethod),
          Content = { }
      };

      CopyHeaders(context, request, _options.FrowardedHeaders.ConvertLegacyHeaders);


      request.RequestUri = proxyTargetUri;
      try
      {
        return await _httpClient.Value.SendAsync(request, HttpCompletionOption.ResponseContentRead);
      }
      catch (Exception e)
      {
        throw new ReverseProxyFailedRequestException(request, e);
      }
    }

    static void CopyHeaders(ICommunicationContext context, HttpRequestMessage request, bool convertLegacyHeaders)
    {
      StringBuilder legacyForward = null;

      void appendParameter(string key, string value)
      {
        if (legacyForward == null) legacyForward = new StringBuilder();
        if (legacyForward.Length > 0) legacyForward.Append(";");
        legacyForward.Append(key).Append("=").Append(value);
      }

      foreach (var header in context.Request.Headers)
      {
        if (convertLegacyHeaders)
        {
          if (header.Key.Equals("X-Forwarded-Host", StringComparison.OrdinalIgnoreCase))
          {
            appendParameter("host", header.Value);
            continue;
          }

          if (header.Key.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase))
          {
            appendParameter("for", header.Value);
            continue;
          }

          if (header.Key.Equals("X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase))
          {
            appendParameter("proto", header.Value);
            continue;
          }
        }

        if (header.Key.Equals("host", StringComparison.OrdinalIgnoreCase)) continue;
        request.Headers.Add(header.Key, header.Value);
      }

      if (convertLegacyHeaders && legacyForward?.Length > 0)
      {
        request.Headers.Add("forwarded", legacyForward.ToString());
      }

      request.Headers.Add("forwarded", CurrentForwarded(context));
    }

    public static Uri GetProxyTargetUri(Uri requestUri, TemplatedUriMatch requestUriMatch, string target)
    {
      var sourceBaseUri = new Uri(requestUri, "/");
      var destinationBaseUri = new Uri(new Uri(target), "/");
      var targetTemplate = new UriTemplate(target);
      var requestQs = requestUriMatch;

      var targetUri = targetTemplate.BindByName(
          destinationBaseUri,
          new NameValueCollection
          {
              requestQs.Match.PathSegmentVariables,
              requestQs.Match.QueryStringVariables
          });

      var targetUriBuilder = new UriBuilder(targetUri);

      var sourceTemplateQsKeysWithVars = requestQs.Match.Template.QueryString
          .Where(qs => qs.Type == UriTemplate.SegmentType.Variable)
          .Select(qs => qs.Key)
          .ToList();

      var requestQueryNotMappedToSourceTemplateVars =
          string.Join("&",
              requestQs.Match.QueryString
                  .Where(qs => !sourceTemplateQsKeysWithVars.Contains(qs.Key, StringComparer.OrdinalIgnoreCase))
                  .Select(qs => qs.ToString()));

      if (requestQueryNotMappedToSourceTemplateVars.Length > 0)
      {
        var query = targetUriBuilder.Query;
        if (query.StartsWith("?")) query = query.Substring(1);
        if (query.Length > 0) query += "&";
        query += requestQueryNotMappedToSourceTemplateVars;
        targetUriBuilder.Query = query;
      }

      var proxyTargetUri = targetUriBuilder.Uri;
      return proxyTargetUri;
    }

    static string CurrentForwarded(ICommunicationContext context)
    {
      return $"proto={context.Request.Uri.Scheme};host={context.Request.Uri.Host}";
    }
  }
}