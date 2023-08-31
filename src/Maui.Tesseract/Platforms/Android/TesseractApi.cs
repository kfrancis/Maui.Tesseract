using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Com.Googlecode.Tesseract.Android;
using Java.Interop;
using Path = System.IO.Path;

namespace Maui.Tesseract
{
    public class TesseractApi : ITesseractApi
    {
        /// <summary>
        ///     Whitelist of characters to recognize.
        /// </summary>
        public const string VAR_CHAR_WHITELIST = "tessedit_char_whitelist";

        /// <summary>
        ///     Blacklist of characters to not recognize.
        /// </summary>
        public const string VAR_CHAR_BLACKLIST = "tessedit_char_blacklist";

        private readonly AssetsDeployment _assetsDeployment;
        private readonly Context _context;
        private readonly ProgressHandler _progressHandler = new();
        private readonly TessBaseAPI _api;
        private volatile bool _busy;
        private Rectangle? _rect;

        public TesseractApi(Context context, AssetsDeployment assetsDeployment)
        {
            _assetsDeployment = assetsDeployment;
            _context = context;
            _progressHandler.Progress += (sender, e) =>
            {
                OnProgress(e.Progress);
            };
            _api = new TessBaseAPI(_progressHandler);
        }

        public BitmapFactory.Options Options { get; set; } = new BitmapFactory.Options { InSampleSize = 1 };

        public string Text { get; private set; }

        public bool Initialized { get; private set; }

        public async Task<bool> Init(string language, OcrEngineMode? mode = null)
        {
            if (string.IsNullOrEmpty(language))
                return false;
            try
            {
                var path = await CopyAssets();
                var result = mode.HasValue
                    ? _api.Init(path, language, GetOcrEngineMode(mode.Value))
                    : _api.Init(path, language);
                Initialized = result;
                return result;
            }
            catch (Java.Lang.IllegalArgumentException ex)
            {
                Log.Debug("TesseractApi", ex, ex.Message);
                Initialized = false;
                return false;
            }
        }

        public void SetVariable(string key, string value)
        {
            CheckIfInitialized();
            _api.SetVariable(key, value);
        }

        public async Task<bool> SetImage(byte[] data)
        {
            CheckIfInitialized();
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using var bitmap = await BitmapFactory.DecodeByteArrayAsync(data, 0, data.Length, Options);
            return await Recognise(bitmap);
        }

        public async Task<bool> SetImage(string path)
        {
            CheckIfInitialized();
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            using var bitmap = await BitmapFactory.DecodeFileAsync(path, Options);
            return await Recognise(bitmap);
        }

        public async Task<bool> SetImage(Stream stream)
        {
            CheckIfInitialized();
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            using var bitmap = await BitmapFactory.DecodeStreamAsync(stream, null, Options);
            return await Recognise(bitmap);
        }

        public void SetWhitelist(string whitelist)
        {
            CheckIfInitialized();
            _api.SetVariable(VAR_CHAR_WHITELIST, whitelist);
        }

        public void SetBlacklist(string blacklist)
        {
            CheckIfInitialized();
            _api.SetVariable(VAR_CHAR_BLACKLIST, blacklist);
        }

        public void SetRectangle(Rectangle? rect)
        {
            CheckIfInitialized();
            _rect = rect;
        }

        public void SetPageSegmentationMode(PageSegmentationMode mode)
        {
            CheckIfInitialized();
            _api.SetPageSegMode((int)mode);
        }
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _api?.Dispose();
                }
                disposedValue = true;
            }
        }

        ~TesseractApi()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public IEnumerable<Result> Results(Tesseract.PageIteratorLevel level)
        {
            CheckIfInitialized();
            var pageIteratorLevel = GetPageIteratorLevel(level);
            var iterator = _api.ResultIterator;
            if (iterator == null)
                yield break;
            // ReSharper disable once TooWideLocalVariableScope
            int[] boundingBox;
            iterator.Begin();
            do
            {
                boundingBox = iterator.GetBoundingBox(pageIteratorLevel);
                var result = new Result
                {
                    Confidence = iterator.Confidence(pageIteratorLevel),
                    Text = iterator.GetUTF8Text(pageIteratorLevel),
                    Box = new Rectangle(boundingBox[0], boundingBox[1], boundingBox[2] - boundingBox[0], boundingBox[3] - boundingBox[1])
                };
                yield return result;
            } while (iterator.Next(pageIteratorLevel));
        }

        public event EventHandler<ProgressEventArgs> Progress;

        public void Clear()
        {
            _rect = null;
            _api.Clear();
        }

        public Task<bool> Init(string tessDataPath, string language)
        {
            var result = _api.Init(tessDataPath, language);
            Initialized = result;
            return Task.FromResult(result);
        }

        public async Task<bool> Recognise(Bitmap bitmap)
        {
            CheckIfInitialized();
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
            if (_busy)
                return false;
            _busy = true;
            try
            {
                await Task.Run(() =>
                {
                    _api.SetImage(bitmap);
                    if (_rect.HasValue)
                    {
                        _api.SetRectangle((int)_rect.Value.Left, (int)_rect.Value.Top, (int)_rect.Value.Width,
                            (int)_rect.Value.Height);
                    }
                    Text = _api.UTF8Text;
                });
                return true;
            }
            finally
            {
                _busy = false;
            }
        }

        public string GetHOCRText(int page)
        {
            return _api.GetHOCRText(page);
        }

        public void Stop()
        {
            _api.Stop();
        }

        public void End()
        {
            _api.End();
        }

        public void BeginDocument(TessPdfRenderer tessPdfRenderer, string title = null)
        {
            if (title == null)
                _api.BeginDocument(tessPdfRenderer);
            else
                _api.BeginDocument(tessPdfRenderer, title);
        }

        public void EndDocument(TessPdfRenderer tessPdfRenderer)
        {
            _api.EndDocument(tessPdfRenderer);
        }

        public void AddPageToDocument(Com.Googlecode.Leptonica.Android.Pix imageToProcess, string imageToWrite, TessPdfRenderer tessPdfRenderer)
        {
            _api.AddPageToDocument(imageToProcess, imageToWrite, tessPdfRenderer);
        }

        public void ReadConfigFile(string fileName)
        {
            _api.ReadConfigFile(fileName);
        }

        private static int GetOcrEngineMode(OcrEngineMode mode)
        {
            return (int)mode;
        }

        private int GetPageIteratorLevel(Tesseract.PageIteratorLevel level)
        {
            return (int)level;
        }

        private async Task<string> CopyAssets()
        {
            try
            {
                var assetManager = _context.Assets;
                var files = assetManager.List("tessdata");
                var file = _context.GetExternalFilesDir(null);
                string tessdataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tessdata");
                if (!Directory.Exists(tessdataPath))
                {
                    Directory.CreateDirectory(tessdataPath);
                }
                else if (_assetsDeployment == AssetsDeployment.OncePerVersion)
                {
                    var packageInfo = _context.PackageManager.GetPackageInfo(_context.PackageName, 0);
                    var version = packageInfo.VersionName;
                    string versionFilePath = Path.Combine(tessdataPath, "version");
                    if (System.IO.File.Exists(versionFilePath))
                    {
                        string fileVersion = System.IO.File.ReadAllText(versionFilePath);
                        if (version == fileVersion)
                        {
                            Log.Debug("TesseractApi", "Application version didn't change, skipping copying assets");
                            return file.AbsolutePath;  // Assuming 'file' is defined elsewhere in your code
                        }
                        System.IO.File.Delete(versionFilePath);
                    }
                    System.IO.File.WriteAllText(versionFilePath, version);
                }

                Log.Debug("TesseractApi", "Copy assets to " + file.AbsolutePath);

                foreach (var filename in files)
                {
                    using var inStream = assetManager.Open("tessdata/" + filename);
                    string outFilePath = Path.Combine(tessdataPath, filename);
                    if (System.IO.File.Exists(outFilePath))
                    {
                        System.IO.File.Delete(outFilePath);
                    }
                    using var outStream = new FileStream(outFilePath, FileMode.Create);
                    await inStream.CopyToAsync(outStream);
                    await outStream.FlushAsync();
                }
                return file.AbsolutePath;
            }
            catch (Exception ex)
            {
                Log.Error("TesseractApi", ex.Message);
            }
            return null;
        }

        private void OnProgress(int progress)
        {
            var handler = Progress;
            handler?.Invoke(this, new ProgressEventArgs(progress));
        }

        private void CheckIfInitialized()
        {
            if (!Initialized)
                throw new InvalidOperationException("Call Init first");
        }

        private class ProgressHandler : Java.Lang.Object, TessBaseAPI.IProgressNotifier
        {
            private bool disposedValue;

            nint IJavaObject.Handle => throw new NotImplementedException();

            int IJavaPeerable.JniIdentityHashCode => throw new NotImplementedException();

            JniObjectReference IJavaPeerable.PeerReference => throw new NotImplementedException();

            JniPeerMembers IJavaPeerable.JniPeerMembers => throw new NotImplementedException();

            JniManagedPeerStates IJavaPeerable.JniManagedPeerState => throw new NotImplementedException();

            public void OnProgressValues(TessBaseAPI.ProgressValues progress)
            {
                OnProgress(progress.Percent);
            }

            internal event EventHandler<ProgressEventArgs> Progress;

            private void OnProgress(int progress)
            {
                var handler = Progress;
                handler?.Invoke(this, new ProgressEventArgs(progress));
            }

            void TessBaseAPI.IProgressNotifier.OnProgressValues(TessBaseAPI.ProgressValues p0)
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.SetJniIdentityHashCode(int value)
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.SetPeerReference(JniObjectReference reference)
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.SetJniManagedPeerState(JniManagedPeerStates value)
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.UnregisterFromRuntime()
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.DisposeUnlessReferenced()
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.Disposed()
            {
                throw new NotImplementedException();
            }

            void IJavaPeerable.Finalized()
            {
                throw new NotImplementedException();
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects)
                    }

                    // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                    // TODO: set large fields to null
                    disposedValue = true;
                }
            }

            // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
            // ~ProgressHandler()
            // {
            //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            //     Dispose(disposing: false);
            // }

            void IDisposable.Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
    }
}