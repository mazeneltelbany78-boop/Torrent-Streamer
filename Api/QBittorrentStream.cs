using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TorrentStreamer.Api
{
    public class QBittorrentStream : Stream
    {
        private readonly FileStream _fileStream;
        private readonly QBittorrentClient _client;
        private readonly string _hash;
        private readonly string _fileName;
        private readonly long _totalLength;
        private long _position;

        public QBittorrentStream(FileStream fileStream, QBittorrentClient client, string hash, string fileName, long totalLength)
        {
            _fileStream = fileStream;
            _client = client;
            _hash = hash;
            _fileName = fileName;
            _totalLength = totalLength;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _totalLength;

        public override long Position
        {
            get => _position;
            set
            {
                _position = value;
                _fileStream.Position = value;
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int delayMs = 250;
            long availableBytesToRead = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var files = await _client.GetTorrentFilesAsync(_hash);
                    var fileInfo = files?.FirstOrDefault(f => f.Name == _fileName);

                    if (fileInfo != null)
                    {
                        long downloadedBytes = (long)(fileInfo.Size * fileInfo.Progress);
                        
                        if (fileInfo.Progress >= 1.0)
                        {
                            availableBytesToRead = count;
                            break;
                        }
                        else if (downloadedBytes > _position)
                        {
                            availableBytesToRead = Math.Min(count, downloadedBytes - _position);
                            // Only read if we have a reasonable chunk, or if it's the very beginning
                            if (availableBytesToRead >= 8192 || _position == 0 || availableBytesToRead == count)
                            {
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore transient API errors and just retry
                }

                await Task.Delay(delayMs, cancellationToken);
                if (delayMs < 2000) delayMs += 250;
            }

            _fileStream.Position = _position;
            int bytesToRead = (int)Math.Min(count, availableBytesToRead);
            int bytesRead = await _fileStream.ReadAsync(buffer, offset, bytesToRead, cancellationToken);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos = _position;
            switch (origin)
            {
                case SeekOrigin.Begin: newPos = offset; break;
                case SeekOrigin.Current: newPos += offset; break;
                case SeekOrigin.End: newPos = _totalLength + offset; break;
            }
            Position = newPos;
            return newPos;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
