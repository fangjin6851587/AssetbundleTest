#if !ABM_USE_WWW && !ABM_USE_UWREQ
#if UNITY_5_4_OR_NEWER && !UNITY_5_5
#define ABM_USE_UWREQ
#else
#define ABM_USE_WWW
#endif
#endif

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ABM_USE_UWREQ
using UnityEngine.Networking;
#endif

namespace AssetBundles.AssetBundleHttpUtils
{
    internal class MultiRangeDownloader
    {
        static WaitForEndOfFrame sWaitForEndOfFrame = new WaitForEndOfFrame();

        private MultiRangeHttpRequest mHttpRequest;

        public MultiRangeDownloader() { }

        public void CloseDownloadData()
        {
            if (mHttpRequest != null)
            {
                mHttpRequest.Kill();
                mHttpRequest = null;
            }
        }

        public IEnumerator DownloadData(string url, List<HttpRange> rangeList, System.Action<HttpRange> onHttpRangeRequestListener, System.Action<MultiRangeHttpRequestResult> onHttpRequestResultListener, int timeOutTime = 10000)
        {
            url = url.Replace(" ", "%20");

            CloseDownloadData();

            MultiRangeHttpRequestResult result = null;
            Queue<HttpRange> responseRangeQueue = new Queue<HttpRange>();
            int rangeRspIndex = 0;
            mHttpRequest = new MultiRangeHttpRequest(url, rangeList, (webUrl, httpRange) =>
            {
                lock (responseRangeQueue)
                {
                    responseRangeQueue.Enqueue(httpRange);
                }
            }, (webUrl, error) =>
            {
                result = error;
            }, timeOutTime);

            while (rangeRspIndex != rangeList.Count)
            {
                lock (responseRangeQueue)
                {
                    while (responseRangeQueue.Count > 0)
                    {
                        if (onHttpRangeRequestListener != null)
                        {
                            onHttpRangeRequestListener(responseRangeQueue.Dequeue());
                        }
                        rangeRspIndex++;
                    }

                    if (result != null)
                    {
                        break;
                    }
                }
                yield return sWaitForEndOfFrame;
            }

            if (onHttpRequestResultListener != null)
            {
                onHttpRequestResultListener(result);
            }
        }
    }

    internal class Downloader
    {
        static WaitForEndOfFrame sWaitForEndOfFrame = new WaitForEndOfFrame();

        private string mUrl;
        private System.Action<byte[], string> onDownLoaded;

        public Downloader(string url, System.Action<byte[], string> callback)
        {
            mUrl = url;
            onDownLoaded = callback;
        }

        public IEnumerator SendWebRequest()
        {
            string webUrl = mUrl.Replace(" ", "%20");

            byte[] data = null;
            string error;
#if ABM_USE_UWREQ
            UnityWebRequest request = UnityWebRequest.Get(webUrl);
            request.SendWebRequest();

            while (!request.isDone)
            {
                yield return sWaitForEndOfFrame;
            }

            if (request.downloadHandler != null)
            {
                data = request.downloadHandler.data;
            }
            error = request.error;
#else
            WWW www = new WWW(webUrl);
            while (!www.isDone)
            {
                yield return sWaitForEndOfFrame;
            }
            data = www.bytes;
            error = www.error;
#endif

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("Url: " + mUrl + " Error: " + error);
            }

            if (onDownLoaded != null)
            {
                onDownLoaded(data, error);
            }
        }

#if ENABLE_ASYNC_WAIT && NET_4_6

        public async void AsyncSendWebRequest()
        {
            string webUrl = mUrl.Replace(" ", "%20");

            byte[] data = null;
            UnityWebRequest request = UnityWebRequest.Get(webUrl);
            await request.SendWebRequest();

            if (request.downloadHandler != null)
            {
                data = request.downloadHandler.data;
            }
            string error = request.error;

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("Url: " + mUrl + " Error: " + error);
            }
            onDownLoaded?.Invoke(data, error);
        }
#endif
    }
}