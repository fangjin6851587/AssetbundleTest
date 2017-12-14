using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace AssetBundles.AssetBundleHttpUtils
{
    public class WorkerThread : IDisposable
    {
        protected bool mEndLoop;
        protected object mLocker = new object();
        protected Thread mThreadObj;

        public WorkerThread()
        {
            mEndLoop = false;
            mThreadObj = null;

            mThreadObj = new Thread(DefaultDelegate);
        }

        public WorkerThread(bool autoStart)
            : this()
        {
            if (autoStart)
            {
                Start();
            }
        }

        public bool EndLoop
        {
            set
            {
                lock (mLocker)
                {
                    mEndLoop = value;
                }
            }
            get
            {
                bool result = false;
                lock (mLocker)
                {
                    result = mEndLoop;
                }
                return result;
            }
        }

        public bool IsAlive
        {
            get
            {
                bool isAlive = mThreadObj.IsAlive;
                return isAlive;
            }
        }

        public Thread Thread
        {
            get { return mThreadObj; }
        }

        public void Dispose()
        {
            Kill();
        }

        protected virtual void DefaultDelegate()
        {
            try
            {
                while (EndLoop == false)
                {
                    Thread.Sleep(1);
                }
            }
            finally
            {
            }
        }

        public void Kill()
        {
            //Kill is called on client thread - must use cached object
            if (IsAlive == false)
            {
                return;
            }

            EndLoop = true;
        }

        public void Start()
        {
            mThreadObj.Start();
        }
    }


    public class HttpRange
    {
        public byte[] data;
        public string id;
        public int from { get; private set; }
        public int to { get; private set; }

        public override string ToString()
        {
            return string.Format("id: {0}, http range: {1}-{2}", id, from, to);
        }

        public void SetRange(int start, int size)
        {
            from = start;
            to = start + size - 1;
        }
    }

    public class MultiRangeHttpRequestException : Exception
    {
        public MultiRangeHttpRequestException()
        {
        }

        public MultiRangeHttpRequestException(MultiRangeCode code, string message) : base(message)
        {
            multiRangeCode = code;
        }

        public MultiRangeCode multiRangeCode { private set; get; }
    }


    public enum MultiRangeCode
    {
        InvalidPackageData = 100,
        InvalidHeader = 101,
        InvalidRange = 102,
        HttpRangeTooLarge = 200,
        HttpUnsupportdRange = 201,
        OK = 300,
        UnFinished = 400
    }

    public class MultiRangeHttpRequestResult
    {
        public HttpStatusCode httpStatusCode = HttpStatusCode.PartialContent;
        public string message = "";
        public MultiRangeCode multiRangeCode = MultiRangeCode.UnFinished;

        public override string ToString()
        {
            return string.Format("httpStatusCode: {0}, multiRangeCode: {1}, errorDesc: {2}", httpStatusCode,
                multiRangeCode, message);
        }
    }

    public class MultiRangeHttpRequest : WorkerThread
    {
        public const int MAX_HTTP_RANGE_COUNT = 200;
        private readonly List<HttpRange> mRangeList;
        private readonly int mTimeOutMS;

        private readonly string mUrl;
        private Action<string, HttpRange> OnHttpRangeRequestListener;
        private Action<string, MultiRangeHttpRequestResult> OnHttpRequestResultListener;

        public MultiRangeHttpRequest(string url, List<HttpRange> rangeList,
            Action<string, HttpRange> onHttpRangeRequestListener,
            Action<string, MultiRangeHttpRequestResult> onHttpRequestListener, int timeOutTime = 10000)
        {
            mUrl = url;
            mRangeList = new List<HttpRange>();
            if (rangeList != null)
            {
                mRangeList.AddRange(rangeList);
            }

            OnHttpRangeRequestListener = onHttpRangeRequestListener;
            OnHttpRequestResultListener = onHttpRequestListener;
            mTimeOutMS = timeOutTime;

#if UNITY_EDITOR
            Debug.Log("Network Debug - Http multi range request : " + url);
#endif
            Start();
        }

        private static bool CheckValidationResult(object sender, X509Certificate certificate, X509Chain chain,
            SslPolicyErrors errors)
        {
            return true;
        }

        protected override void DefaultDelegate()
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            var result = new MultiRangeHttpRequestResult();
            int rangeListCount = mRangeList.Count;

            try
            {
                if (mRangeList.Count > MAX_HTTP_RANGE_COUNT)
                {
                    throw new MultiRangeHttpRequestException(MultiRangeCode.HttpRangeTooLarge,
                        string.Format("http range too large: range count must be less than {0}.",
                            MAX_HTTP_RANGE_COUNT));
                }

                if (mUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                {
                    ServicePointManager.ServerCertificateValidationCallback = CheckValidationResult;
                    request = WebRequest.Create(mUrl) as HttpWebRequest;
                    request.ProtocolVersion = HttpVersion.Version11;
                }
                else
                {
                    request = (HttpWebRequest) WebRequest.Create(mUrl);
                }

                request.Proxy = null;
                request.Timeout = mTimeOutMS;
                request.Credentials = CredentialCache.DefaultCredentials;

                foreach (var r in mRangeList)
                {
                    request.AddRange(r.from, r.to);
#if UNITY_EDITOR
                    Debug.Log("http request multi range: " + r.ToString());
#endif
                }

                response = (HttpWebResponse) request.GetResponse();


                if (response.StatusCode == HttpStatusCode.OK)
                {
                    throw new MultiRangeHttpRequestException(MultiRangeCode.HttpUnsupportdRange,
                        "http unsupported content range.");
                }

                // buffer for payload
                var buffer = new byte[1024 * 1024];

                // count of last data read from ResponseStream
                int bytesRead = 0;

                HttpRange lastRange = null;

                if (rangeListCount > 1)
                {
                    string boundary = "";

                    var m = Regex.Match(response.ContentType, @"^.*boundary=(?<boundary>.*)$");

                    if (m.Success)
                    {
                        boundary = m.Groups["boundary"].Value;
                    }
                    else
                    {
                        throw new MultiRangeHttpRequestException(MultiRangeCode.InvalidPackageData,
                            string.Format("invalid packet data: no boundary specification found, content_type = {0}.",
                                response.ContentType));
                    }

                    using (var source = response.GetResponseStream())
                    {
                        source.ReadTimeout = mTimeOutMS;
                        using (var ms = new MemoryStream())
                        {
                            // buffer for current range header
                            var header = new byte[200];
                            // next header after x bytes
                            int nextHeader = 0;
                            // current position in header[]
                            int headerPosition = 0;
                            // current position in buffer[]
                            int bufferPosition = 0;
                            // left data to proceed
                            int bytesToProceed = 0;
                            // count of last data written to target file
                            int bytesWritten = 0;
                            // size of processed header data
                            int headerSize = 0;


                            while (!mEndLoop && rangeListCount > 0 &&
                                   (bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                bufferPosition = 0;
                                bytesToProceed = bytesRead;
                                headerSize = 0;

                                while (rangeListCount > 0 && bytesToProceed > 0)
                                {
                                    if (nextHeader == 0)
                                    {
                                        bool readNew = false;
                                        for (;
                                            headerPosition < header.Length;
                                            headerPosition++, bufferPosition++, headerSize++)
                                        {
                                            if (bytesToProceed > headerPosition && bufferPosition < bytesRead)
                                            {
                                                header[headerPosition] = buffer[bufferPosition];
                                                if (headerPosition >= 4 &&
                                                    header[headerPosition - 3] == 0x0d &&
                                                    header[headerPosition - 2] == 0x0a &&
                                                    header[headerPosition - 1] == 0x0d &&
                                                    header[headerPosition] == 0x0a)
                                                {
                                                    string rangeHeader =
                                                        Encoding.ASCII.GetString(header, 0, headerPosition + 1);

#if UNITY_EDITOR
                                                    Debug.Log("range header: " + rangeHeader + "boundary: " + boundary);
#endif

                                                    var mm = Regex.Match(rangeHeader,
                                                        @"^(\r\n|)(--)?" + boundary +
                                                        @".*?(?<from>\d+)\s*-\s*(?<to>\d+)/.*\r\n\r\n",
                                                        RegexOptions.Singleline);

                                                    if (mm.Success)
                                                    {
                                                        int rangeStart = Convert.ToInt32(mm.Groups["from"].Value);
                                                        int rangeEnd = Convert.ToInt32(mm.Groups["to"].Value);

                                                        nextHeader = rangeEnd - rangeStart + 1;

                                                        bufferPosition++;

                                                        bytesToProceed -= headerSize + 1;

                                                        headerPosition = 0;
                                                        headerSize = 0;

#if UNITY_EDITOR
                                                        Debug.Log(string.Format("http response mutil range: {0}-{1}", rangeStart, rangeEnd));
#endif

                                                        lastRange = GetRange(rangeStart, rangeEnd);
                                                        if (lastRange == null)
                                                        {
                                                            throw new MultiRangeHttpRequestException(
                                                                MultiRangeCode.InvalidRange,
                                                                string.Format("invalid range: {0}-{1}.", rangeStart,
                                                                    rangeEnd));
                                                        }

                                                        break;
                                                    }

                                                    throw new MultiRangeHttpRequestException(
                                                        MultiRangeCode.InvalidHeader,
                                                        string.Format(
                                                            "invalid header: missing range specification, header = {0}.",
                                                            rangeHeader));
                                                }
                                            }
                                            else
                                            {
                                                readNew = true;
                                                break;
                                            }
                                        }

                                        if (readNew)
                                        {
                                            bytesToProceed = 0;
                                            break;
                                        }

                                        if (nextHeader == 0)
                                        {
                                            throw new MultiRangeHttpRequestException(MultiRangeCode.InvalidPackageData,
                                                "invalid packet data: no range-header found.");
                                        }
                                    }

                                    bytesWritten = nextHeader > bytesToProceed ? bytesToProceed : nextHeader;
                                    ms.Write(buffer, bufferPosition, bytesWritten);
                                    bytesToProceed -= bytesWritten;
                                    nextHeader -= bytesWritten;
                                    bufferPosition += bytesWritten;

                                    if (nextHeader == 0)
                                    {
                                        if (lastRange != null)
                                        {
#if UNITY_EDITOR
                                            Debug.Log("end read http multi range: " + lastRange.ToString() + " time: " + System.DateTime.Now);
#endif
                                            lastRange.data = ms.ToArray();
                                            mRangeList.Remove(lastRange);
                                            rangeListCount = mRangeList.Count;
                                            if (OnHttpRangeRequestListener != null)
                                            {
                                                OnHttpRangeRequestListener(mUrl, lastRange);
                                            }
                                        }
                                        ClearMemoryStream(ms);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    rangeListCount = 1;
                    using (var source = response.GetResponseStream())
                    {
                        source.ReadTimeout = mTimeOutMS;
                        using (var ms = new MemoryStream())
                        {
                            if (mRangeList.Count > 0)
                            {
                                lastRange = mRangeList[0];
                            }
                            else
                            {
                                lastRange = new HttpRange();
                            }
#if UNITY_EDITOR
                            Debug.Log("start read http single range: " + lastRange.ToString());
#endif
                            while (!mEndLoop && (bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                ms.Write(buffer, 0, bytesRead);
                            }

                            rangeListCount = 0;
                            lastRange.data = ms.ToArray();
#if UNITY_EDITOR
                            Debug.Log("end read http multi range: " + lastRange.ToString());
#endif
                            if (OnHttpRangeRequestListener != null)
                            {
                                OnHttpRangeRequestListener(mUrl, lastRange);
                            }
                            ClearMemoryStream(ms);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                result.message = ex.Message;
            }
            catch (MultiRangeHttpRequestException ex)
            {
                result.multiRangeCode = ex.multiRangeCode;
                result.message = ex.Message;
            }
            catch (Exception ex)
            {
                result.message = ex.Message;
            }
            finally
            {
                if (response != null)
                {
                    result.httpStatusCode = response.StatusCode;
                    response.Close();
                    response = null;
                }
                if (request != null)
                {
                    request.Abort();
                    request = null;
                }

                if (rangeListCount == 0)
                {
                    result.multiRangeCode = MultiRangeCode.OK;
                }

                if (result != null)
                {
#if UNITY_EDITOR
                    Debug.Log("multi range request result from url: " + mUrl);
                Debug.Log(result.ToString());
#endif
                    if (OnHttpRequestResultListener != null)
                    {
                        OnHttpRequestResultListener(mUrl, result);
                    }
                }
                OnHttpRangeRequestListener = null;
                OnHttpRequestResultListener = null;
            }
        }

        private void ClearMemoryStream(MemoryStream source)
        {
            var buffer = source.GetBuffer();
            Array.Clear(buffer, 0, buffer.Length);
            source.Position = 0;
            source.SetLength(0);
        }

        private HttpRange GetRange(int from, int to)
        {
            foreach (var range in mRangeList)
            {
                if (range.from == from && range.to == to)
                {
                    return range;
                }
            }

            return null;
        }
    }
}