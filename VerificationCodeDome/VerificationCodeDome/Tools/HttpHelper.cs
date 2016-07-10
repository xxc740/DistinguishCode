using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;

namespace VerificationCodeDome.Tools
{
    public class HttpHelper
    {
        public HttpHelper()
        {
            CookieContainer = new CookieContainer();
            Encoding = Encoding.UTF8;
        }

        private ConcurrentDictionary<string, int> urlTryList = new ConcurrentDictionary<string, int>();

        public CookieContainer CookieContainer { get; set; }

        public string PostData { private get; set; }

        public Encoding Encoding { set; private get; }

        public string CodePath { get; set; }

        /// <summary>
        /// Gets or sets the file save path.
        /// </summary>
        /// <value>
        /// 文件保存路径
        /// </value>
        public string FileSavePath { get; set; }

        /// <summary>
        /// 回调时间
        /// </summary>
        public Action<string, string> CallBackAction;

        /// <summary>
        /// 异步请求
        /// </summary>
        /// <param name="url">请求地址</param>
        /// <param name="trytimes">错误重试次数</param>
        public void AsynRequest(string url, int trytimes = 3)
        {
            Trace.TraceInformation(string.Concat("开始异步请求：", url));
            urlTryList.TryAdd(url, trytimes);
            var request = WebRequest.Create(url) as HttpWebRequest;
            if (request == null)
                return;
            request.Headers.Add("Accept-Encoding", "gzip,deflate,sdch");
            request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.8");
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                             DecompressionMethods.None;
            request.Credentials = CredentialCache.DefaultNetworkCredentials;
            request.UseDefaultCredentials = false;
            request.KeepAlive = false;
            request.PreAuthenticate = false;
            request.ProtocolVersion = HttpVersion.Version10;
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/34.0.1847.116 Safari/537.36";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.Timeout = 1000*60*3;
            request.CookieContainer = CookieContainer;

            if (!string.IsNullOrEmpty(PostData))
            {
                request.ContentType = "application/x-www-form-urlencoded";
                request.Method = "POST";
                request.BeginGetRequestStream(GetRequestStreamCallback, request);
            }
            else
            {
                request.AllowWriteStreamBuffering = false;
                request.BeginGetResponse(GetResponseCallback, request);
            }
        }

        /// <summary>
        /// 开始对用来写入数据的Stream对象的异步请求
        /// </summary>
        /// <param name="re">The re.</param>
        private void GetRequestStreamCallback(IAsyncResult re)
        {
            var request = re.AsyncState as HttpWebRequest;
            if (request == null)
                return;
            var postStream = request.EndGetRequestStream(re);
            var byteArray = Encoding.GetBytes(PostData);
            postStream.Write(byteArray, 0, PostData.Length);
            postStream.Close();
            request.BeginGetResponse(GetResponseCallback, request);
        }

        /// <summary>
        /// 开始对Internet资源的异步请求
        /// </summary>
        /// <param name="re">The re.</param>
        private void GetResponseCallback(IAsyncResult re)
        {
            var request = re.AsyncState as HttpWebRequest;
            if (request == null)
                return;
            try
            {
                using (var response = request.EndGetResponse(re) as HttpWebResponse)
                {
                    if (response != null)
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            Trace.TraceError(String.Concat("请求地址：", request.RequestUri, "错误码：", response.StatusCode));
                            return;
                        }

                        using (var streamResponse = response.GetResponseStream())
                        {
                            if (streamResponse != null)
                            {
                                if (!IsText(response.ContentType))
                                {
                                    var contentEncodingStr = response.ContentEncoding;
                                    var contentEncoding = Encoding;
                                    if (!String.IsNullOrEmpty(contentEncodingStr))
                                        contentEncoding = Encoding.GetEncoding(contentEncodingStr);
                                    using (var streamRead = new StreamReader(streamResponse, contentEncoding))
                                    {
                                        var str = streamRead.ReadToEnd();
                                        if (CallBackAction != null && !String.IsNullOrEmpty(str))
                                            CallBackAction.BeginInvoke(str, request.RequestUri.ToString(), (s) => { },
                                                null);
                                    }
                                }
                                else
                                {
                                    var fileName = String.Concat(DateTime.Now.ToString("yyyyMMdd"), "/",
                                        DateTime.Now.ToString("yyyyMMddHHmmssffff"), ".jpg");
                                    var fileDirectory = Path.Combine(FileSavePath, DateTime.Now.ToString("yyyyMMdd"));
                                    CodePath = Path.Combine(FileSavePath, fileName);
                                    if (!Directory.Exists(fileDirectory))
                                        Directory.CreateDirectory(fileDirectory);

                                    //下载文件
                                    using (
                                        var fileStream = new FileStream(Path.Combine(FileSavePath, fileName),
                                            FileMode.Create))
                                    {
                                        var buffer = new byte[2048];
                                        int readLength;
                                        do
                                        {
                                            readLength = streamResponse.Read(buffer, 0, buffer.Length);
                                            fileStream.Write(buffer, 0, readLength);
                                        } while (readLength != 0);
                                    }
                                    if (CallBackAction != null && !String.IsNullOrEmpty(fileName))
                                        CallBackAction.BeginInvoke(fileName, request.RequestUri.ToString(), (s) => { },
                                            null);
                                }
                            }
                        }
                        response.Close();
                    }
                }
            }
            catch (WebException ex)
            {
                Trace.TraceError(String.Concat("请求地址：", request.RequestUri, "失败信息：", ex.Message));
                var toUrl = request.RequestUri.ToString();
                int tryTimes;
                if (urlTryList.TryGetValue(toUrl, out tryTimes))
                {
                    urlTryList.TryUpdate(toUrl, tryTimes, tryTimes - 1);
                    if (tryTimes - 1 <= 0)
                    {
                        urlTryList.TryRemove(toUrl, out tryTimes);
                        return;
                    }
                    AsynRequest(toUrl);
                }
            }
            finally
            {
                request.Abort();
            }
        }

        public string SyncRequest(string url, int tryTimes = 3)
        {
            Trace.TraceInformation(string.Concat("开始同步请求：", url));
            urlTryList.TryAdd(url, tryTimes);
            var request = WebRequest.Create(url) as HttpWebRequest;
            if (request == null)
                return string.Empty;
            request.Headers.Add("Accept-Encoding", "gzip,deflate,sdch");
            request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.8");
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                             DecompressionMethods.None;
            request.Credentials = CredentialCache.DefaultNetworkCredentials;
            request.UseDefaultCredentials = false;
            request.KeepAlive = false;
            request.PreAuthenticate = false;
            request.ProtocolVersion = HttpVersion.Version10;
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/34.0.1847.116 Safari/537.36";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.Timeout = 1000*60*3;
            request.CookieContainer = CookieContainer;
            request.AllowAutoRedirect = true;

            if (!string.IsNullOrEmpty(PostData))
            {
                request.ContentType = "application/x-www-form-urlencoded";
                request.Method = "POST";
                using (var postStream = request.GetRequestStream())
                {
                    var byteArray = Encoding.GetBytes(PostData);
                    postStream.Write(byteArray, 0, PostData.Length);
                    postStream.Close();
                }
            }
            else
            {
                request.AllowWriteStreamBuffering = false;
            }

            try
            {
                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    if (response != null)
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            Trace.TraceError(string.Concat("请求地址：", request.RequestUri, "失败，错误码：", response.StatusCode));
                            return string.Empty;
                        }
                        using (var streamResponse = response.GetResponseStream())
                        {
                            if (streamResponse != null)
                            {
                                if (!IsText(response.ContentType))
                                {
                                    var contentEncodingStr = response.ContentEncoding;
                                    var contentEncoding = Encoding;
                                    if (string.IsNullOrEmpty(contentEncodingStr))
                                        contentEncoding = Encoding.GetEncoding(contentEncodingStr);
                                    var streamRead = new StreamReader(streamResponse, contentEncoding);
                                    var str = streamRead.ReadToEnd();
                                    if (CallBackAction != null && !string.IsNullOrEmpty(str))
                                        CallBackAction.BeginInvoke(str, request.RequestUri.ToString(), (s) => { }, null);
                                    return str;
                                }

                                var fileName = string.Concat(DateTime.Now.ToString("yyyyMMdd"), "/",
                                    DateTime.Now.ToString("yyyyMMddHHmmssffff"),
                                    Path.GetExtension(request.RequestUri.AbsoluteUri));
                                var fileDirectory = Path.Combine(FileSavePath, DateTime.Now.ToString("yyyyMMdd"));
                                if (!Directory.Exists(fileDirectory))
                                    Directory.CreateDirectory(fileDirectory);

                                //下载文件
                                using (
                                    var fileStream = new FileStream(Path.Combine(FileSavePath, fileName),
                                        FileMode.Create))
                                {
                                    var buffer = new byte[2048];
                                    int readLength;
                                    do
                                    {
                                        readLength = streamResponse.Read(buffer, 0, buffer.Length);
                                        fileStream.Write(buffer, 0, readLength);
                                    } while (readLength != 0);
                                }

                                if (CallBackAction != null && !string.IsNullOrEmpty(fileName))
                                    CallBackAction.BeginInvoke(fileName, request.RequestUri.ToString(), (s) => { }, null);

                                return fileName;
                            }
                        }

                        response.Close();
                    }
                }
            }
            catch (WebException ex)
            {
                Trace.TraceError(string.Concat("请求地址：", request.RequestUri, "失败信息：", ex.Message));
                var toUrl = request.RequestUri.ToString();
                if (urlTryList.TryGetValue(toUrl, out tryTimes))
                {
                    urlTryList.TryUpdate(toUrl, tryTimes, tryTimes - 1);
                    if (tryTimes - 1 <= 0)
                    {
                        urlTryList.TryRemove(toUrl, out tryTimes);
                        Trace.TraceError(string.Concat("请求地址重试失败：", request.RequestUri));
                        return string.Empty;
                    }

                    SyncRequest(toUrl);
                }
            }
            finally
            {
                request.Abort();
            }

            return string.Empty;
        }

        public Bitmap GetCheckCode(string url, int tryTime = 3)
        {
            Trace.TraceInformation(string.Concat("开始同步请求：", url));
            urlTryList.TryAdd(url, tryTime);
            var request = WebRequest.Create(url) as HttpWebRequest;
            if (request == null)
                return null;
            request.Headers.Add("Accept-Encoding", "gzip,deflate,sdch");
            request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.8");
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                             DecompressionMethods.None;
            request.Credentials = CredentialCache.DefaultNetworkCredentials;
            request.UseDefaultCredentials = false;
            request.KeepAlive = false;
            request.PreAuthenticate = false;
            request.ProtocolVersion = HttpVersion.Version10;
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/34.0.1847.116 Safari/537.36";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.Timeout = 1000*60*3;
            request.CookieContainer = CookieContainer;
            request.AllowAutoRedirect = true;

            if (!string.IsNullOrEmpty(PostData))
            {
                request.ContentType = "application/x-www-form-urlencoded";
                request.Method = "POST";
                using (var postStream = request.GetRequestStream())
                {
                    var byteArray = Encoding.GetBytes(PostData);
                    postStream.Write(byteArray, 0, PostData.Length);
                    postStream.Close();
                }
            }
            else
            {
                request.AllowWriteStreamBuffering = false;
            }

            try
            {
                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    if (response != null)
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            Trace.TraceError(string.Concat("请求地址：", request.RequestUri, "失败,错误码：", response.StatusCode));
                            return null;
                        }

                        using (var streamResponse = response.GetResponseStream())
                        {
                            if (streamResponse != null)
                            {
                                return (Bitmap) Bitmap.FromStream(streamResponse);
                            }
                        }

                        response.Close();
                    }
                }
            }
            catch (WebException ex)
            {
                Trace.TraceError(string.Concat("请求地址：", request.RequestUri, "失败信息：", ex.Message));
                var toUrl = request.RequestUri.ToString();
                if (urlTryList.TryGetValue(toUrl, out tryTime))
                {
                    urlTryList.TryUpdate(toUrl, tryTime, tryTime - 1);
                    if (tryTime - 1 <= 0)
                    {
                        urlTryList.TryRemove(toUrl, out tryTime);
                        Trace.TraceError(string.Concat("请求地址重试失败：", request.RequestUri));
                        return null;
                    }

                    GetCheckCode(toUrl);
                }
            }
            finally
            {
                request.Abort();
            }

            return null;
        }

        private static bool IsText(string contentType)
        {
            var fileContentType = new List<string>
            {
                "image/Bmp",
                "image/gif",
                "image/jpeg",
                "image/png",
                "image/tiff",
                "application/octet-stream"
            };

            return fileContentType.Contains(contentType);
        }
    }
}
