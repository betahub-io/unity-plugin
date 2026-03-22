#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BetaHub
{
    public struct VideoSegment
    {
        public byte[] Data;
        public string MimeType;
        public string FileExtension;
        public string ContentType;
    }

    public static class WebGLRecorderBridge
    {
        [DllImport("__Internal")]
        public static extern int BetaHubRecorder_Init(int targetFps, int maxWidth, int maxHeight);

        [DllImport("__Internal")]
        public static extern void BetaHubRecorder_StartRecording();

        [DllImport("__Internal")]
        public static extern void BetaHubRecorder_StopRecording();

        [DllImport("__Internal")]
        public static extern void BetaHubRecorder_SetPaused(int paused);

        [DllImport("__Internal")]
        public static extern int BetaHubRecorder_IsRecording();

        [DllImport("__Internal")]
        public static extern int BetaHubRecorder_GetSegmentCount();

        [DllImport("__Internal")]
        public static extern int BetaHubRecorder_GetSegmentSize(int index);

        [DllImport("__Internal")]
        public static extern void BetaHubRecorder_CopySegmentData(int index, IntPtr dest);

        [DllImport("__Internal")]
        public static extern string BetaHubRecorder_GetSegmentMimeType(int index);

        [DllImport("__Internal")]
        public static extern void BetaHubRecorder_FreeSegments();

        /// <summary>
        /// Collects all recorded segments from JavaScript memory into managed C# byte arrays.
        /// Frees the JS-side segment data after copying.
        /// </summary>
        public static List<VideoSegment> CollectSegments()
        {
            var segments = new List<VideoSegment>();
            int count = BetaHubRecorder_GetSegmentCount();

            for (int i = 0; i < count; i++)
            {
                int size = BetaHubRecorder_GetSegmentSize(i);
                if (size <= 0) continue;

                // Allocate managed buffer and pin it for the copy
                byte[] data = new byte[size];
                GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    BetaHubRecorder_CopySegmentData(i, handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }

                string mimeType = BetaHubRecorder_GetSegmentMimeType(i);

                // Derive file extension and content type from mime type
                string ext = "mp4";
                string contentType = "video/mp4";
                if (mimeType != null && mimeType.Contains("webm"))
                {
                    ext = "webm";
                    contentType = "video/webm";
                }

                segments.Add(new VideoSegment
                {
                    Data = data,
                    MimeType = mimeType ?? "video/mp4",
                    FileExtension = ext,
                    ContentType = contentType,
                });
            }

            // Free JS-side segment memory
            BetaHubRecorder_FreeSegments();

            Debug.Log($"[WebGLRecorderBridge] Collected {segments.Count} segments ({count} available)");
            return segments;
        }
    }
}
#endif
