using System;
using System.Collections;
using YoutubeExplode;
using YoutubeExplode.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using YoutubeExplode.Playlists;
using System.Collections.Generic;
using System.IO;
using YoutubeExplode.Videos;
using System.Net;
using System.Drawing;
using YoutubeExplode.Videos.Streams;
using System.Reflection.PortableExecutable;
using FFMpegCore;
using FFMpegCore.Enums;

namespace YoutubeDownloader
{
    // program for downloading a youtube playlist
    internal class Program
    {
        // can't make the main async
        public static Task Main(string[] args) => new Program().MainAsync(args);

        public async Task MainAsync(string[] args)
        {
            int test = 0;
            if (args[1] == "all")
            {
                args[1] = "0";
            }
            if (!Int32.TryParse(args[1], out test))
            {
                throw new ArgumentException("Second argument should be a whole number");
            }
            var youtube = new YoutubeClient();
            var listOfAudio = new ArrayList();
            // giving the playlist id
            IReadOnlyList<PlaylistVideo> videos;
            if (Convert.ToInt32(args[1]) == 0)
            {
                 videos = await youtube.Playlists.GetVideosAsync(args[0]).CollectAsync();
            }else
            {
                videos = await youtube.Playlists.GetVideosAsync(args[0]).CollectAsync(Convert.ToInt32(args[1]));
            }
            
            foreach (var video in videos)
            {
                var info = new ArrayList() { video.Url, video.Title };
                listOfAudio.Add(info);
            }
            #region downloads the video as an audio file
            int counter = 0;
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "downloads")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "downloads"));
            }
            DateTime start = DateTime.Now;
            int max = listOfAudio.Count;
            WebClient client = new WebClient();
            foreach (ArrayList url in listOfAudio)
            {
                counter++;

                if (!System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "downloads", RemoveSpecialCharacters(url[1].ToString()) + ".mp3")))
                {
                    #region sets up the audio downloading
                    var manifest = await youtube.Videos.Streams.GetManifestAsync(url[0].ToString());
                    var audioOptions = manifest.GetAudioStreams();
                    #endregion
                    List<IAudioStreamInfo> cleanList = new List<IAudioStreamInfo>();
                    IAudioStreamInfo audioManifest;
                    foreach (var audio in audioOptions)
                    {
                        if (audio.Container.Name == "mp4" || audio.Container.Name == "mp3")
                        {
                            cleanList.Add(audio);
                        }
                    }
                    #region gets lower quality if there is a third parameter
                    if (args.Length > 2 && args[2].ToString() == "low")
                    {
                        audioManifest = cleanList[cleanList.Count - 2];
                    }
                    #endregion
                    #region gets the highest audio quality
                    else
                    {
                        audioManifest=cleanList[cleanList.Count - 1];

                    }
                    #endregion


                    
                    // saves the video to the path current directory and then downloads with the music video name


                    await youtube.Videos.Streams.DownloadAsync(audioManifest, Path.Combine(Directory.GetCurrentDirectory(), "downloads", RemoveSpecialCharacters(url[1].ToString()) + "." + audioManifest.Container.Name));
                    ConvertAudioFile(RemoveSpecialCharacters(url[1].ToString()) + "." + audioManifest.Container.Name);
                    Console.WriteLine(Path.Combine(Directory.GetCurrentDirectory(), "downloads", RemoveSpecialCharacters(url[1].ToString()) + ".mp3") + ": "
                       + Math.Round(Convert.ToDecimal(counter) / max * 100, 2) + "% done"
                       + ", " + CalculateEstimatedDownloadTime(start, counter, max));
                    

                    

                }
                else
                {
                    max -= counter;
                    counter = 0;
                    start = DateTime.Now;
                }
                
            }
            #endregion
        }
        //removes the special character out of a string
        public static string RemoveSpecialCharacters(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_' || c == ' ')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        public static string CalculateEstimatedDownloadTime(DateTime startTime, int countDone, int countTodo)
        {
            DateTime now = DateTime.Now;
            TimeSpan timeTaken = now.Subtract(startTime);
            double totalMil = timeTaken.TotalMilliseconds;
            double averageTimePerItem = (double)totalMil / countDone;
            double estimatedTimeLeft = (countTodo - countDone) * averageTimePerItem;
            int estimatedHours = Convert.ToInt32(Math.Truncate(estimatedTimeLeft / (1000 * 60 * 60)));
            estimatedTimeLeft = estimatedTimeLeft - estimatedHours * (1000 * 60 * 60);
            int minutes = Convert.ToInt32(Math.Truncate(estimatedTimeLeft / (1000 * 60)));
            estimatedTimeLeft = estimatedTimeLeft - minutes * (1000 * 60);
            int seconds = Convert.ToInt32(Math.Truncate(estimatedTimeLeft / 1000 ));
            return estimatedHours + " hours " + minutes + " minutes " + seconds + " seconds left";
        }
        public static async void ConvertAudioFile(string fileName)
        {
            await FFMpegArguments
                        .FromFileInput(Path.Combine(Directory.GetCurrentDirectory(), "downloads", fileName))
                        .OutputToFile(Path.Combine(Directory.GetCurrentDirectory(), "downloads", fileName.Split(".")[0] + ".mp3"), true)
                        .ProcessAsynchronously();
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "downloads",fileName ));
        }
    }
}
