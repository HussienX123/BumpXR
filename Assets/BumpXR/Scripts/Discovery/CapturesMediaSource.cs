using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BumpXR.Core;
using BumpXR.Transfer;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace BumpXR.Discovery
{
    public enum CapturesState { NotOpened, RequestingPermission, Loading, Ready, Empty, Denied, Error, NeedsPermission }

    /// <summary>Abstraction for Android MediaStore + runtime permission so lazy permission behavior is unit-testable in Editor.</summary>
    public interface ICapturesBackend
    {
        bool HasPermission();
        Task<bool> RequestPermissionAsync();
        Task<List<MediaItem>> QueryRecentImagesAsync(int maxCount);
        Task<byte[]> LoadThumbnailBytesAsync(string contentUri, int maxSidePx);
        byte[] ReadContentStreamBytes(string contentUri, long maxBytes);
    }

    /// <summary>
    /// IMediaSource over device photos/screenshots via Android MediaStore + ContentResolver streams (no file paths).
    /// Requests android.permission.READ_MEDIA_IMAGES when the gallery explicitly asks for photo access.
    /// Docs:
    /// - https://developers.meta.com/vr/resources/permissions-review-required
    /// - https://developers.meta.com/vr/documentation/unity/unity-spatial-data-perm
    /// - https://developers.meta.com/vr/resources/vrc-quest-security-2
    /// </summary>
    public sealed class CapturesMediaSource : IMediaSource
    {
        public const string ReadMediaImagesPermission = "android.permission.READ_MEDIA_IMAGES";
        public const int MaxCaptures = 60;
        public const int ThumbnailSizePx = 256;
        public const string DeniedMessage = "Allow photo access in your Quest app settings, then try again.";
        public const string EmptyMessage = "Take a screenshot on your Quest, then refresh to see it here.";

        private readonly ICapturesBackend _backend;
        private readonly List<MediaItem> _items = new List<MediaItem>();
        private readonly SemaphoreSlim _thumbGate = new SemaphoreSlim(2, 2);
        private Task<bool> _inflightOpen;

        public IReadOnlyList<MediaItem> Items => _items;
        public CapturesState State { get; private set; } = CapturesState.NotOpened;
        public string StatusMessage { get; private set; } = "";
        public int PermissionRequestCount { get; private set; }
        public int FullReadCount { get; private set; }

        public CapturesMediaSource(ICapturesBackend backend = null)
        {
            _backend = backend ?? CreateDefaultBackend();
        }

        /// <summary>Query the newest 60 MediaStore JPEG/PNG captures, optionally requesting photo access.</summary>
        public Task<bool> EnsureLoadedAsync(bool forceRefresh = false, bool requestPermission = true)
        {
            if (_inflightOpen != null && !_inflightOpen.IsCompleted) return _inflightOpen;
            if (!forceRefresh && (State == CapturesState.Ready || State == CapturesState.Empty))
                return Task.FromResult(true);
            _inflightOpen = OpenCoreAsync(requestPermission);
            return _inflightOpen;
        }

        private async Task<bool> OpenCoreAsync(bool requestPermission)
        {
            try
            {
                bool permitted = _backend.HasPermission();
                if (!permitted)
                {
                    if (!requestPermission)
                    {
                        _items.Clear();
                        State = State == CapturesState.Denied ? CapturesState.Denied : CapturesState.NeedsPermission;
                        StatusMessage = State == CapturesState.Denied
                            ? DeniedMessage
                            : "Allow photo access to browse and share your Quest captures.";
                        return false;
                    }

                    State = CapturesState.RequestingPermission;
                    StatusMessage = "Allow photo access to browse your Quest captures...";
                    PermissionRequestCount++;
                    permitted = await _backend.RequestPermissionAsync();
                }

                if (!permitted)
                {
                    _items.Clear();
                    State = CapturesState.Denied;
                    StatusMessage = DeniedMessage;
                    return false;
                }

                State = CapturesState.Loading;
                StatusMessage = "Loading captures...";
                List<MediaItem> queried = await _backend.QueryRecentImagesAsync(MaxCaptures);
                _items.Clear();
                if (queried != null)
                {
                    for (int i = 0; i < queried.Count && _items.Count < MaxCaptures; i++)
                    {
                        MediaItem m = queried[i];
                        if (m == null || !ReceivedStore.IsAllowedMime(m.MimeType)) continue;
                        if (m.Size <= 0 || m.Size > LocalNetworkTransferService.MaxFileBytes) continue;
                        _items.Add(m);
                    }
                }

                if (_items.Count == 0)
                {
                    State = CapturesState.Empty;
                    StatusMessage = EmptyMessage;
                }
                else
                {
                    State = CapturesState.Ready;
                    StatusMessage = "";
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BumpXR] My Captures query failed: " + e.Message);
                _items.Clear();
                State = CapturesState.Error;
                StatusMessage = "Your captures couldn't be loaded. Try refreshing.";
                return false;
            }
        }

        public byte[] ReadBytes(MediaItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id))
                throw new ArgumentException("Invalid capture item.");
            FullReadCount++;
            return _backend.ReadContentStreamBytes(item.Id, LocalNetworkTransferService.MaxFileBytes);
        }

        public Task<byte[]> ReadThumbnailBytesAsync(MediaItem item) => ReadPreviewBytesAsync(item, ThumbnailSizePx);

        public async Task<byte[]> ReadPreviewBytesAsync(MediaItem item, int maxSidePx)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) return null;
            await _thumbGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await _backend.LoadThumbnailBytesAsync(item.Id, maxSidePx).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BumpXR] Capture thumbnail load failed ({item.FileName}): {e.Message}");
                return null;
            }
            finally
            {
                _thumbGate.Release();
            }
        }

        private static ICapturesBackend CreateDefaultBackend()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidCapturesBackend();
#else
            return new EditorEmptyCapturesBackend();
#endif
        }

        private sealed class EditorEmptyCapturesBackend : ICapturesBackend
        {
            public bool HasPermission() => true;
            public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
            public Task<List<MediaItem>> QueryRecentImagesAsync(int maxCount) => Task.FromResult(new List<MediaItem>());
            public Task<byte[]> LoadThumbnailBytesAsync(string contentUri, int maxSidePx) => Task.FromResult<byte[]>(null);
            public byte[] ReadContentStreamBytes(string contentUri, long maxBytes) => throw new NotSupportedException("No Android ContentResolver in Editor.");
        }

#if UNITY_ANDROID
        private sealed class AndroidCapturesBackend : ICapturesBackend
        {
            private readonly string _readPermission;

            public AndroidCapturesBackend()
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                _readPermission = version.GetStatic<int>("SDK_INT") >= 33
                    ? ReadMediaImagesPermission
                    : Permission.ExternalStorageRead;
            }

            public bool HasPermission() => Permission.HasUserAuthorizedPermission(_readPermission);

            public Task<bool> RequestPermissionAsync()
            {
                if (HasPermission()) return Task.FromResult(true);

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var callbacks = new PermissionCallbacks();
                // Per Meta Spatial Data Permission docs, subscribe only to PermissionGranted and PermissionDenied
                // (PermissionDeniedAndDontAskAgain is unreliable on newer Android; omitting it routes denials to PermissionDenied).
                callbacks.PermissionGranted += _ => tcs.TrySetResult(true);
                callbacks.PermissionDenied += _ => tcs.TrySetResult(false);
                Permission.RequestUserPermission(_readPermission, callbacks);
                return tcs.Task;
            }

            public Task<List<MediaItem>> QueryRecentImagesAsync(int maxCount)
            {
                return Task.Run(() =>
                {
                    AndroidJNI.AttachCurrentThread();
                    try
                    {
                        var results = new List<MediaItem>();
                        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                        using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                        using var mediaClass = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                        using var externalUri = mediaClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                        using var contentUris = new AndroidJavaClass("android.content.ContentUris");

                        string[] projection = { "_id", "_display_name", "mime_type", "_size", "date_added" };
                        const string selection = "mime_type IN (?, ?)";
                        string[] selectionArgs = { ReceivedStore.JpegMime, ReceivedStore.PngMime };
                        const string sortOrder = "date_added DESC, _id DESC";

                        using var cursor = resolver.Call<AndroidJavaObject>("query", externalUri, projection, selection, selectionArgs, sortOrder);
                        if (cursor == null) return results;

                        while (results.Count < maxCount && cursor.Call<bool>("moveToNext"))
                        {
                            long id = cursor.Call<long>("getLong", 0);
                            string displayName = cursor.Call<string>("getString", 1);
                            string mime = cursor.Call<string>("getString", 2);
                            long size = cursor.Call<long>("getLong", 3);
                            if (!ReceivedStore.IsAllowedMime(mime) || size <= 0 || size > LocalNetworkTransferService.MaxFileBytes)
                                continue;

                            using var itemUri = contentUris.CallStatic<AndroidJavaObject>("withAppendedId", externalUri, id);
                            string uriStr = itemUri.Call<string>("toString");
                            string fallbackName = "capture_" + id + ReceivedStore.ExtensionFor(mime);
                            results.Add(new MediaItem
                            {
                                Id = uriStr,
                                FileName = string.IsNullOrEmpty(displayName) ? fallbackName : displayName,
                                MimeType = mime,
                                Size = size
                            });
                        }
                        return results;
                    }
                    finally
                    {
                        AndroidJNI.DetachCurrentThread();
                    }
                });
            }

            public Task<byte[]> LoadThumbnailBytesAsync(string contentUri, int maxSidePx)
            {
                return Task.Run(() =>
                {
                    AndroidJNI.AttachCurrentThread();
                    try
                    {
                        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                        using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                        using var uriClass = new AndroidJavaClass("android.net.Uri");
                        using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", contentUri);
                        using var sizeObj = new AndroidJavaObject("android.util.Size", maxSidePx, maxSidePx);
                        using var bitmap = resolver.Call<AndroidJavaObject>("loadThumbnail", uri, sizeObj, null);
                        if (bitmap == null) return null;

                        try
                        {
                            using var baos = new AndroidJavaObject("java.io.ByteArrayOutputStream");
                            using var formatClass = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat");
                            using var jpeg = formatClass.GetStatic<AndroidJavaObject>("JPEG");
                            bitmap.Call<bool>("compress", jpeg, 80, baos);
                            sbyte[] signed = baos.Call<sbyte[]>("toByteArray");
                            if (signed == null || signed.Length == 0) return null;
                            var bytes = new byte[signed.Length];
                            Buffer.BlockCopy(signed, 0, bytes, 0, signed.Length);
                            return bytes;
                        }
                        finally
                        {
                            bitmap.Call("recycle");
                        }
                    }
                    finally
                    {
                        AndroidJNI.DetachCurrentThread();
                    }
                });
            }

            public byte[] ReadContentStreamBytes(string contentUri, long maxBytes)
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", contentUri);
                using var input = resolver.Call<AndroidJavaObject>("openInputStream", uri);
                if (input == null) throw new IOException("ContentResolver returned null stream for " + contentUri);

                IntPtr jBuf = AndroidJNI.NewSByteArray(65536);
                try
                {
                    using var baos = new AndroidJavaObject("java.io.ByteArrayOutputStream");
                    IntPtr inCls = AndroidJNI.GetObjectClass(input.GetRawObject());
                    IntPtr outCls = AndroidJNI.GetObjectClass(baos.GetRawObject());
                    try
                    {
                        IntPtr readMid = AndroidJNI.GetMethodID(inCls, "read", "([B)I");
                        IntPtr writeMid = AndroidJNI.GetMethodID(outCls, "write", "([BII)V");
                        var readArgs = new jvalue[1];
                        readArgs[0].l = jBuf;
                        var writeArgs = new jvalue[3];
                        writeArgs[0].l = jBuf;
                        writeArgs[1].i = 0;

                        long total = 0;
                        while (true)
                        {
                            int n = AndroidJNI.CallIntMethod(input.GetRawObject(), readMid, readArgs);
                            if (n <= 0) break;
                            total += n;
                            if (total > maxBytes) throw new IOException("Capture exceeds maximum allowed file size.");
                            writeArgs[2].i = n;
                            AndroidJNI.CallVoidMethod(baos.GetRawObject(), writeMid, writeArgs);
                        }

                        sbyte[] signed = baos.Call<sbyte[]>("toByteArray");
                        var bytes = new byte[signed.Length];
                        Buffer.BlockCopy(signed, 0, bytes, 0, signed.Length);
                        return bytes;
                    }
                    finally
                    {
                        AndroidJNI.DeleteLocalRef(inCls);
                        AndroidJNI.DeleteLocalRef(outCls);
                    }
                }
                finally
                {
                    AndroidJNI.DeleteLocalRef(jBuf);
                    try { input.Call("close"); } catch { }
                }
            }
        }
#endif
    }
}

