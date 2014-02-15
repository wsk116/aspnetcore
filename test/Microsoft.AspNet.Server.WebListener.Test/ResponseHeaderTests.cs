﻿// -----------------------------------------------------------------------
// <copyright file="ResponseHeaderTests.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNet.FeatureModel;
using Microsoft.AspNet.HttpFeature;
using Microsoft.AspNet.PipelineCore;
using Xunit;

namespace Microsoft.AspNet.Server.WebListener.Tests
{
    using AppFunc = Func<object, Task>;

    public class ResponseHeaderTests
    {
        private const string Address = "http://localhost:8080/";

        [Fact]
        public async Task ResponseHeaders_ServerSendsDefaultHeaders_Success()
        {
            using (CreateServer(env =>
            {
                return Task.FromResult(0);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.Equal(2, response.Headers.Count());
                Assert.False(response.Headers.TransferEncodingChunked.HasValue);
                Assert.True(response.Headers.Date.HasValue);
                Assert.Equal("Microsoft-HTTPAPI/2.0", response.Headers.Server.ToString());
                Assert.Equal(1, response.Content.Headers.Count());
                Assert.Equal(0, response.Content.Headers.ContentLength);
            }
        }

        [Fact]
        public async Task ResponseHeaders_ServerSendsCustomHeaders_Success()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var responseInfo = httpContext.GetFeature<IHttpResponseInformation>();
                var responseHeaders = responseInfo.Headers;
                responseHeaders["Custom-Header1"] = new string[] { "custom1, and custom2", "custom3" };
                return Task.FromResult(0);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.Equal(3, response.Headers.Count());
                Assert.False(response.Headers.TransferEncodingChunked.HasValue);
                Assert.True(response.Headers.Date.HasValue);
                Assert.Equal("Microsoft-HTTPAPI/2.0", response.Headers.Server.ToString());
                Assert.Equal(new string[] { "custom1, and custom2", "custom3" }, response.Headers.GetValues("Custom-Header1"));
                Assert.Equal(1, response.Content.Headers.Count());
                Assert.Equal(0, response.Content.Headers.ContentLength);
            }
        }

        [Fact]
        public async Task ResponseHeaders_ServerSendsConnectionClose_Closed()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var responseInfo = httpContext.GetFeature<IHttpResponseInformation>();
                var responseHeaders = responseInfo.Headers;
                responseHeaders["Connection"] = new string[] { "Close" };
                return Task.FromResult(0);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.True(response.Headers.ConnectionClose.Value);
                Assert.Equal(new string[] { "close" }, response.Headers.GetValues("Connection"));
            }
        }
        /* TODO:
        [Fact]
        public async Task ResponseHeaders_SendsHttp10_Gets11Close()
        {
            using (CreateServer(env =>
            {
                env["owin.ResponseProtocol"] = "HTTP/1.0";
                return Task.FromResult(0);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.Equal(new Version(1, 1), response.Version);
                Assert.True(response.Headers.ConnectionClose.Value);
                Assert.Equal(new string[] { "close" }, response.Headers.GetValues("Connection"));
            }
        }

        [Fact]
        public async Task ResponseHeaders_SendsHttp10WithBody_Gets11Close()
        {
            using (CreateServer(env =>
            {
                env["owin.ResponseProtocol"] = "HTTP/1.0";
                return env.Get<Stream>("owin.ResponseBody").WriteAsync(new byte[10], 0, 10);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.Equal(new Version(1, 1), response.Version);
                Assert.False(response.Headers.TransferEncodingChunked.HasValue);
                Assert.False(response.Content.Headers.Contains("Content-Length"));
                Assert.True(response.Headers.ConnectionClose.Value);
                Assert.Equal(new string[] { "close" }, response.Headers.GetValues("Connection"));
            }
        }
        */

        [Fact]
        public async Task ResponseHeaders_HTTP10Request_Gets11Close()
        {
            using (CreateServer(env =>
            {
                return Task.FromResult(0);
            }))
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Address);
                    request.Version = new Version(1, 0);
                    HttpResponseMessage response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    Assert.Equal(new Version(1, 1), response.Version);
                    Assert.True(response.Headers.ConnectionClose.Value);
                    Assert.Equal(new string[] { "close" }, response.Headers.GetValues("Connection"));
                }
            }
        }

        [Fact]
        public async Task ResponseHeaders_HTTP10Request_RemovesChunkedHeader()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var responseInfo = httpContext.GetFeature<IHttpResponseInformation>();
                var responseHeaders = responseInfo.Headers;
                responseHeaders["Transfer-Encoding"] = new string[] { "chunked" };
                return responseInfo.Body.WriteAsync(new byte[10], 0, 10);
            }))
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Address);
                    request.Version = new Version(1, 0);
                    HttpResponseMessage response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    Assert.Equal(new Version(1, 1), response.Version);
                    Assert.False(response.Headers.TransferEncodingChunked.HasValue);
                    Assert.False(response.Content.Headers.Contains("Content-Length"));
                    Assert.True(response.Headers.ConnectionClose.Value);
                    Assert.Equal(new string[] { "close" }, response.Headers.GetValues("Connection"));
                }
            }
        }

        [Fact]
        public async Task Headers_FlushSendsHeaders_Success()
        {
            using (CreateServer(
                env =>
                {
                    var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                    var responseInfo = httpContext.GetFeature<IHttpResponseInformation>();
                    var responseHeaders = responseInfo.Headers;
                    responseHeaders.Add("Custom1", new string[] { "value1a", "value1b" });
                    responseHeaders.Add("Custom2", new string[] { "value2a, value2b" });
                    var body = responseInfo.Body;
                    body.Flush();
                    responseInfo.StatusCode = 404; // Ignored
                    responseHeaders.Add("Custom3", new string[] { "value3a, value3b", "value3c" }); // Ignored
                    return Task.FromResult(0);
                }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.Equal(5, response.Headers.Count()); // Date, Server, Chunked

                Assert.Equal(2, response.Headers.GetValues("Custom1").Count());
                Assert.Equal("value1a", response.Headers.GetValues("Custom1").First());
                Assert.Equal("value1b", response.Headers.GetValues("Custom1").Skip(1).First());
                Assert.Equal(1, response.Headers.GetValues("Custom2").Count());
                Assert.Equal("value2a, value2b", response.Headers.GetValues("Custom2").First());
            }
        }

        [Fact]
        public async Task Headers_FlushAsyncSendsHeaders_Success()
        {
            using (CreateServer(
                async env =>
                {
                    var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                    var responseInfo = httpContext.GetFeature<IHttpResponseInformation>();
                    var responseHeaders = responseInfo.Headers;
                    responseHeaders.Add("Custom1", new string[] { "value1a", "value1b" });
                    responseHeaders.Add("Custom2", new string[] { "value2a, value2b" });
                    var body = responseInfo.Body;
                    await body.FlushAsync();
                    responseInfo.StatusCode = 404; // Ignored
                    responseHeaders.Add("Custom3", new string[] { "value3a, value3b", "value3c" }); // Ignored
                }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                response.EnsureSuccessStatusCode();
                Assert.Equal(5, response.Headers.Count()); // Date, Server, Chunked

                Assert.Equal(2, response.Headers.GetValues("Custom1").Count());
                Assert.Equal("value1a", response.Headers.GetValues("Custom1").First());
                Assert.Equal("value1b", response.Headers.GetValues("Custom1").Skip(1).First());
                Assert.Equal(1, response.Headers.GetValues("Custom2").Count());
                Assert.Equal("value2a, value2b", response.Headers.GetValues("Custom2").First());
            }
        }

        private IDisposable CreateServer(AppFunc app)
        {
            IDictionary<string, object> properties = new Dictionary<string, object>();
            IList<IDictionary<string, object>> addresses = new List<IDictionary<string, object>>();
            properties["host.Addresses"] = addresses;

            IDictionary<string, object> address = new Dictionary<string, object>();
            addresses.Add(address);

            address["scheme"] = "http";
            address["host"] = "localhost";
            address["port"] = "8080";
            address["path"] = string.Empty;

            return OwinServerFactory.Create(app, properties);
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string uri)
        {
            using (HttpClient client = new HttpClient())
            {
                return await client.GetAsync(uri);
            }
        }
    }
}
