using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using YARG.Util;

namespace YARG.Server {
	public class Client {
		public delegate void SignalAction(string signal);
		public event SignalAction SignalEvent;

		public FileInfo remoteCache;
		public string remotePath;

		public string AlbumCoversPath => Path.Combine(remotePath, "_album_covers");

		private Thread thread;
		private TcpClient client;

		private ConcurrentQueue<string> requests = new();
		private ConcurrentQueue<string> signals = new();

		public void Start(string ip) {
			remotePath = Path.Combine(Application.persistentDataPath, "remote");

			// Make sure remote path exists
			var dirInfo = new DirectoryInfo(remotePath);
			if (!dirInfo.Exists) {
				dirInfo.Create();
			}

			// Make sure `album_covers` folder exists
			dirInfo = new DirectoryInfo(AlbumCoversPath);
			if (!dirInfo.Exists) {
				dirInfo.Create();
			}

			client = new TcpClient(ip, 6145);

			thread = new Thread(ClientThread);
			thread.Start();

			// Bind events for application close
			Application.quitting += () => Stop();
		}

		private void ClientThread() {
			var stream = client.GetStream();

			// Request cache
			Send(stream, "ReqCache");

			// Read cache from server
			remoteCache = new(Path.Combine(remotePath, "yarg_cache.json"));
			ReadFile(stream, remoteCache);

			// Wait until request
			while (true) {
				if (requests.TryDequeue(out var request)) {
					Send(stream, request);

					if (request.StartsWith("ReqSong,")) {
						// Read zipped song from server
						string zipPath = Path.Combine(remotePath, "download.zip");
						ReadFile(stream, new(zipPath));

						// When done, unzip file
						string folderName = Utils.Hash(request[8..]);
						ZipFile.ExtractToDirectory(zipPath, Path.Combine(remotePath, folderName));

						// Delete zip
						new FileInfo(zipPath).Delete();

						// Send signal
						signals.Enqueue($"DownloadDone,{folderName}");
					} else if (request.StartsWith("ReqAlbumCover,")) {
						// Read album.png from server
						string hash = Utils.Hash(request[14..]);
						string pngPath = Path.Combine(AlbumCoversPath, $"{hash}.png");
						ReadFile(stream, new(pngPath));

						// Send signal
						signals.Enqueue($"AlbumCoverDone,{hash}");
					}
				}

				// Prevent CPU burn
				Thread.Sleep(25);
			}
		}

		private void Send(NetworkStream stream, string str) {
			var send = Encoding.UTF8.GetBytes(str);
			stream.Write(send, 0, send.Length);
			stream.Flush();
		}

		private void ReadFile(NetworkStream stream, FileInfo output) {
			const int BUF_SIZE = 81920;

			// Wait until data is available
			while (!stream.DataAvailable) {
				Thread.Sleep(100);
			}

			// Get file size
			var buffer = new byte[sizeof(long)];
			stream.Read(buffer, 0, sizeof(long));
			long size = BitConverter.ToInt64(buffer);

			// If the size is zero, the file did not exist on server
			if (size <= 0) {
				return;
			}

			// Copy data to disk
			// We can't use CopyTo on a infinite stream (like NetworkStream)
			long totalRead = 0;
			var fileBuf = new byte[BUF_SIZE];
			using var fs = output.OpenWrite();
			while (totalRead < size) {
				int bytesRead = stream.Read(fileBuf, 0, BUF_SIZE);
				fs.Write(fileBuf, 0, bytesRead);
				totalRead += bytesRead;
			}
		}

		public void Stop() {
			// Close client (if connected to server)
			if (client == null) {
				return;
			}

			thread.Abort();

			// Send "End" packet
			var stream = client.GetStream();
			var send = Encoding.UTF8.GetBytes("End");
			stream.Write(send, 0, send.Length);
			stream.Flush();
			client.Close();

			// Delete remote folder
			Directory.Delete(remotePath, true);
		}

		public void CheckForSignals() {
			while (signals.Count > 0) {
				if (signals.TryDequeue(out var signal)) {
					SignalEvent?.Invoke(signal);
				}
			}
		}

		public void RequestDownload(string path) {
			// See first if the song is already downloaded
			var folderName = Utils.Hash(path);
			var dir = new DirectoryInfo(Path.Combine(remotePath, folderName));
			if (dir.Exists) {
				// If so, send the signal that it has finished downloading
				signals.Enqueue($"DownloadDone,{folderName}");
				return;
			}

			// Otherwise, we have to request it
			requests.Enqueue($"ReqSong,{path}");
		}

		public void RequestAlbumCover(string path) {
			// See first if the album cover is already downloaded
			var folderName = Utils.Hash(path);
			var coverFile = new FileInfo(Path.Combine(AlbumCoversPath, $"{folderName}.png"));
			if (coverFile.Exists) {
				// If so, send the signal that it has finished downloading
				signals.Enqueue($"AlbumCoverDone,{folderName}");
				return;
			}

			// Otherwise, we have to request it
			requests.Enqueue($"ReqAlbumCover,{path}");
		}
	}
}