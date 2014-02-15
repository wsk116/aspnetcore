﻿// -----------------------------------------------------------------------
// <copyright file="ResponseSendFileTests.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.FeatureModel;
using Microsoft.AspNet.HttpFeature;
using Microsoft.AspNet.PipelineCore;
using Xunit;

namespace Microsoft.AspNet.Server.WebListener.Tests
{
    using AppFunc = Func<object, Task>;

    public class ResponseSendFileTests
    {
        private const string Address = "http://localhost:8080/";
        private static readonly string AbsoluteFilePath = Environment.CurrentDirectory + "\\Microsoft.AspNet.Server.WebListener.dll";
        private static readonly string RelativeFilePath = "Microsoft.AspNet.Server.WebListener.dll";
        private static readonly long FileLength = new FileInfo(AbsoluteFilePath).Length;
        
        [Fact]
        public async Task ResponseSendFile_SupportKeys_Present()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                try
                {
                    /* TODO:
                    IDictionary<string, object> capabilities = env.Get<IDictionary<string, object>>("server.Capabilities");
                    Assert.NotNull(capabilities);

                    Assert.Equal("1.0", capabilities.Get<string>("sendfile.Version"));

                    IDictionary<string, object> support = capabilities.Get<IDictionary<string, object>>("sendfile.Support");
                    Assert.NotNull(support);

                    Assert.Equal("Overlapped", support.Get<string>("sendfile.Concurrency"));
                    */

                    var sendFile = httpContext.GetFeature<IHttpSendFile>();
                    Assert.NotNull(sendFile);
                }
                catch (Exception ex)
                {
                    byte[] body = Encoding.UTF8.GetBytes(ex.ToString());
                    httpContext.Response.Body.Write(body, 0, body.Length);
                }
                return Task.FromResult(0);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> ignored;
                Assert.True(response.Content.Headers.TryGetValues("content-length", out ignored), "Content-Length");
                Assert.False(response.Headers.TransferEncodingChunked.HasValue, "Chunked");
                Assert.Equal(0, response.Content.Headers.ContentLength);
                Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            }
        }

        [Fact]
        public async Task ResponseSendFile_MissingFile_Throws()
        {
            ManualResetEvent waitHandle = new ManualResetEvent(false);
            bool? appThrew = null;
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                try
                {
                    sendFile.SendFileAsync(string.Empty, 0, null, CancellationToken.None).Wait();
                    appThrew = false;
                }
                catch (Exception)
                {
                    appThrew = true;
                    throw;
                }
                finally
                {
                    waitHandle.Set();
                }
                return Task.FromResult(0);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.True(waitHandle.WaitOne(100));
                Assert.True(appThrew.HasValue, "appThrew.HasValue");
                Assert.True(appThrew.Value, "appThrew.Value");
            }
        }
        
        [Fact]
        public async Task ResponseSendFile_NoHeaders_DefaultsToChunked()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> ignored;
                Assert.False(response.Content.Headers.TryGetValues("content-length", out ignored), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value, "Chunked");
                Assert.Equal(FileLength, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_RelativeFile_Success()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                return sendFile.SendFileAsync(RelativeFilePath, 0, null, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> ignored;
                Assert.False(response.Content.Headers.TryGetValues("content-length", out ignored), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value, "Chunked");
                Assert.Equal(FileLength, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_Chunked_Chunked()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                httpContext.Response.Headers["Transfer-EncodinG"] = "CHUNKED";
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.False(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(FileLength, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_MultipleChunks_Chunked()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                httpContext.Response.Headers["Transfer-EncodinG"] = "CHUNKED";
                sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None).Wait();
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.False(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(FileLength * 2, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ChunkedHalfOfFile_Chunked()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, FileLength / 2, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.False(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(FileLength / 2, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ChunkedOffsetOutOfRange_Throws()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                return sendFile.SendFileAsync(AbsoluteFilePath, 1234567, null, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(500, (int)response.StatusCode);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ChunkedCountOutOfRange_Throws()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, 1234567, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(500, (int)response.StatusCode);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ChunkedCount0_Chunked()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, 0, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.False(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(0, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ContentLength_PassedThrough()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                httpContext.Response.Headers["Content-lenGth"] = FileLength.ToString();
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.True(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.Equal(FileLength.ToString(), contentLength.First());
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Equal(FileLength, response.Content.ReadAsByteArrayAsync().Result.Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ContentLengthSpecific_PassedThrough()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                httpContext.Response.Headers["Content-lenGth"] = "10";
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, 10, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.True(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.Equal("10", contentLength.First());
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Equal(10, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }

        [Fact]
        public async Task ResponseSendFile_ContentLength0_PassedThrough()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var sendFile = httpContext.GetFeature<IHttpSendFile>();
                httpContext.Response.Headers["Content-lenGth"] = "0";
                return sendFile.SendFileAsync(AbsoluteFilePath, 0, 0, CancellationToken.None);
            }))
            {
                HttpResponseMessage response = await SendRequestAsync(Address);
                Assert.Equal(200, (int)response.StatusCode);
                IEnumerable<string> contentLength;
                Assert.True(response.Content.Headers.TryGetValues("content-length", out contentLength), "Content-Length");
                Assert.Equal("0", contentLength.First());
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Equal(0, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        }
        
        private IDisposable CreateServer(AppFunc app)
        {
            IList<IDictionary<string, object>> addresses = new List<IDictionary<string, object>>();
            IDictionary<string, object> properties = new Dictionary<string, object>();
            properties["host.Addresses"] = addresses;

            IDictionary<string, object> address = new Dictionary<string, object>();
            addresses.Add(address);

            address["scheme"] = "http";
            address["host"] = "localhost";
            address["port"] = "8080";
            address["path"] = string.Empty;
            
            OwinServerFactory.Initialize(properties);

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
