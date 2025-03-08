using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;
namespace YoutubeDownloader
{
    internal class Program
    {
        static readonly char[] characters = Path.GetInvalidFileNameChars();
        static int completedDownloads = 0;
        static object lockObj = new object();
        static DateTime startTime;

        // can't make the main async
        public static Task Main(string[] args) => new Program().MainAsync(args);

        public async Task MainAsync(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: YoutubeDownloader <playlist-url> <limit|all> [low]");
                return;
            }

            int limit = 0;
            if (args[1].ToLower() == "all")
            {
                limit = 0; // 0 means all videos
            }
            else if (!int.TryParse(args[1], out limit))
            {
                throw new ArgumentException("Second argument should be a whole number or 'all'");
            }

            var youtube = new YoutubeClient();

            // Create downloads directory if it doesn't exist
            string downloadDir = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
            if (!Directory.Exists(downloadDir))
            {
                Directory.CreateDirectory(downloadDir);
            }

            Console.WriteLine("Fetching playlist information...");

            // Get videos from playlist
            IReadOnlyList<PlaylistVideo> videos;
            if (limit == 0)
            {
                videos = await youtube.Playlists.GetVideosAsync(args[0]).CollectAsync();
            }
            else
            {
                videos = await youtube.Playlists.GetVideosAsync(args[0]).CollectAsync(limit);
            }

            Console.WriteLine($"Found {videos.Count} videos in playlist");
            Console.WriteLine("Starting downloads...");

            startTime = DateTime.Now;

            // Determine maximum degree of parallelism
            int maxParallelDownloads = Environment.ProcessorCount * 2; // Adjust based on your needs

            // Use a semaphore to limit concurrent downloads
            using (var semaphore = new SemaphoreSlim(maxParallelDownloads, maxParallelDownloads))
            {
                bool useLowQuality = args.Length > 2 && args[2].ToLower() == "low";

                // Create tasks for all videos
                var downloadTasks = videos.Select(video => DownloadVideoAsync(
                    youtube,
                    video,
                    downloadDir,
                    useLowQuality,
                    videos.Count,
                    semaphore)).ToArray();

                // Wait for all downloads to complete
                await Task.WhenAll(downloadTasks);
            }

            Console.WriteLine("\nAll downloads completed!");
            TimeSpan elapsedTime = DateTime.Now - startTime;
            Console.WriteLine($"Total time: {elapsedTime.Hours}h {elapsedTime.Minutes}m {elapsedTime.Seconds}s");
        }

        private async Task DownloadVideoAsync(
            YoutubeClient youtube,
            PlaylistVideo video,
            string downloadDir,
            bool useLowQuality,
            int totalCount,
            SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync(); // Wait until we can download another video

            try
            {
                string safeTitle = RemoveSpecialCharacters(video.Title);
                string filePath = Path.Combine(downloadDir, safeTitle + ".mp3");

                // Skip if already downloaded
                if (File.Exists(filePath))
                {
                    UpdateProgress(totalCount);
                    return;
                }

                // Get stream info
                var manifest = await youtube.Videos.Streams.GetManifestAsync(video.Url);
                var audioStreams = manifest.GetAudioStreams()
                    .Where(a => a.Container.Name == "mp4" || a.Container.Name == "mp3")
                    .ToList();

                if (audioStreams.Count == 0)
                {
                    Console.WriteLine($"No suitable audio stream found for: {video.Title}");
                    return;
                }

                // Select audio quality
                IAudioStreamInfo audioStream;
                if (useLowQuality && audioStreams.Count > 1)
                {
                    audioStream = audioStreams[audioStreams.Count - 2];
                }
                else
                {
                    audioStream = audioStreams.Last();
                }

                // Download the audio
                await youtube.Videos.Streams.DownloadAsync(audioStream, filePath);

                // Update and display progress
                UpdateProgress(totalCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading {video.Title}: {ex.Message}");
            }
            finally
            {
                semaphore.Release(); // Release the semaphore so another download can start
            }
        }

        private void UpdateProgress(int totalCount)
        {
            int current;
            lock (lockObj)
            {
                completedDownloads++;
                current = completedDownloads;
            }

            decimal percentage = Math.Round((decimal)current / totalCount * 100, 2);

            string estimatedTimeLeft = "Calculating...";
            if (current > 0)
            {
                estimatedTimeLeft = CalculateEstimatedTime(startTime, current, totalCount);
            }

            Console.WriteLine($"Progress: {current}/{totalCount} ({percentage}%) - {estimatedTimeLeft}");
        }

        // Removes special characters from a string
        public static string RemoveSpecialCharacters(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if (!characters.Contains(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // Calculate estimated download time
        public static string CalculateEstimatedTime(DateTime startTime, int countDone, int countTodo)
        {
            TimeSpan timeTaken = DateTime.Now - startTime;
            double averageTimePerItem = timeTaken.TotalMilliseconds / countDone;
            double estimatedTimeLeft = (countTodo - countDone) * averageTimePerItem;

            // Convert to hours, minutes, seconds
            int hours = (int)(estimatedTimeLeft / (1000 * 60 * 60));
            estimatedTimeLeft -= hours * (1000 * 60 * 60);

            int minutes = (int)(estimatedTimeLeft / (1000 * 60));
            estimatedTimeLeft -= minutes * (1000 * 60);

            int seconds = (int)(estimatedTimeLeft / 1000);

            return $"{hours}h {minutes}m {seconds}s remaining";
        }
    }
}