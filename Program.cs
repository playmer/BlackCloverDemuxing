using ByteSizeLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace BlackCloverPreprocessing
{
    public static class MyExtensions
    {
        public static string Back(this List<string> list)
        {
            return list.Count != 0 ? list[list.Count - 1] : null;
        }


        public static string Join(this List<string> list)
        {
            return String.Join("\r\n", list.ToArray());
        }

        public static List<string> RemoveAllOf(this List<string> list, Predicate<string> match)
        {
            list.RemoveAll(match);
            return list;
        }

        public static void WaitAndClear(this List<Process> list)
        {
            list.ForEach(extraction => extraction.WaitForExit());
            list.Clear();
        }
    }

    // Common Properties
    struct CommonMediaInfo
    {
        static readonly Dictionary<TrackType, int> cCodecPosition = new Dictionary<TrackType, int> {
                { TrackType.Video, 6 },
                { TrackType.Audio, 5 },
                { TrackType.Subtitle, 5 }
        };

        static readonly Dictionary<TrackType, int> cLanguagePosition = new Dictionary<TrackType, int> {
                { TrackType.Video, 7 },
                { TrackType.Audio, 6 },
                { TrackType.Subtitle, 6 }
        };

        public enum TrackType
        {
            Video, Audio, Subtitle
        }

        public CommonMediaInfo(TrackType aType, List<string> aTrackPropset)
        {
            TrackNumber = System.Int32.Parse(aTrackPropset[0].Split(' ')[9].Split(')')[0]);
            TrackCodec = GetCodec(aType, aTrackPropset);
            Language = GetLanguage(aType, aTrackPropset);
        }

        static string GetCodec(TrackType aType, List<string> aTrackPropset)
        {
            return aTrackPropset[cCodecPosition[aType]].Split(' ')[2];
        }

        static string GetLanguage(TrackType aType, List<string> aTrackPropset)
        {
            return aTrackPropset[cLanguagePosition[aType]].Split(' ')[1];
        }

        public int TrackNumber;
        public string TrackCodec;
        public string Language;
    }

    struct VideoInfo
    {
        public VideoInfo(List<string> aTrackPropset)
        {
            mMediaInfo = new CommonMediaInfo(CommonMediaInfo.TrackType.Video, aTrackPropset);
        }

        public CommonMediaInfo mMediaInfo;
    }

    struct AudioInfo
    {
        public AudioInfo(List<string> aTrackPropset)
        {
            mMediaInfo = new CommonMediaInfo(CommonMediaInfo.TrackType.Audio, aTrackPropset);
        }

        public CommonMediaInfo mMediaInfo;
    }

    struct SubtitleInfo
    {
        public SubtitleInfo(List<string> aTrackPropset)
        {
            mMediaInfo = new CommonMediaInfo(CommonMediaInfo.TrackType.Subtitle, aTrackPropset);
        }

        public CommonMediaInfo mMediaInfo;
    }

    class MkvInfo
    {
        public MkvInfo(List<string> aTracks)
        {
            mVideoInfos = new List<VideoInfo>();
            mAudioInfos = new List<AudioInfo>();
            mSubtitleInfos = new List<SubtitleInfo>();

            foreach (var track in aTracks)
            {
                // Get Track properties
                var properties = track.Split("|  + ").ToList();
                properties.RemoveAt(0); //Empty Line

                if (properties[2].StartsWith("Track type: video"))
                    mVideoInfos.Add(new VideoInfo(properties));
                else if (properties[2].StartsWith("Track type: audio"))
                    mAudioInfos.Add(new AudioInfo(properties));
                else if (properties[2].StartsWith("Track type: subtitles"))
                    mSubtitleInfos.Add(new SubtitleInfo(properties));
            }
        }

        public List<VideoInfo> mVideoInfos;
        public List<AudioInfo> mAudioInfos;
        public List<SubtitleInfo> mSubtitleInfos;
    }



    class Program
    {

        static MkvInfo GetMkvInfo(string aFile)
        {
            var builder = new StringBuilder();
            var process = RunProcess("mkvinfo", aFile, builder);
            process.WaitForExit();
            var tracks = builder
                .ToString()
                .Split("\r\n")
                .ToList()
                .RemoveAllOf(line => line.StartsWith("| + EBML void: "))
                .Join()
                .Split("|+ ")
                .First(line => line.StartsWith("Tracks"))
                .Split("| + Track\r\n")
                .ToList();

            // First one is just the beginning of the track section.
            tracks.RemoveAt(0);

            return new MkvInfo(tracks); ;
        }

        static Process RunProcess(string aProgram, string aCommandLine, StringBuilder stringBuilder = null)
        {
            Console.WriteLine($"{aProgram} {aCommandLine}");

            var process = new Process();
            process.StartInfo.FileName = aProgram;
            process.StartInfo.Arguments = aCommandLine;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;

            if (null != stringBuilder)
                process.OutputDataReceived += (sender, args) => stringBuilder.AppendLine(args.Data);
            else
                process.OutputDataReceived += (sender, args) => Console.WriteLine("received output: {0}", args.Data);

            process.Start();
            process.BeginOutputReadLine();
            return process;
        }

        static void DtsToFlac(string aFile)
        {
            string episodeName = Path.GetFileNameWithoutExtension(aFile);
            string intermediateEpisodeFolder = Path.Combine("D:\\BlackCloverFanEdit\\2_Intermediate\\", episodeName);
            string readyToEditEpisodeFolder = Path.Combine("D:\\BlackCloverFanEdit\\3_ReadyToEdit\\", episodeName);
            string intermediateFilePath = Path.Combine(intermediateEpisodeFolder, Path.GetFileNameWithoutExtension(aFile));
            string finalFilePath = Path.Combine(readyToEditEpisodeFolder, Path.GetFileNameWithoutExtension(aFile));

            var mkvInfo = GetMkvInfo(aFile);
            return;

            Directory.CreateDirectory(intermediateEpisodeFolder);
            Directory.CreateDirectory(readyToEditEpisodeFolder);

            var processes = new List<Process>();

            ////////////////////////////////////////////////////////////////
            // Subtitles
            {
                ////////////////////////////////////////////////////////////////
                // Extract sup files
                var supPaths = new List<string>();
                foreach (var track in mkvInfo.mSubtitleInfos)
                {
                    supPaths.Add($"{intermediateFilePath}_Track_{track.mMediaInfo.TrackNumber}.sup");
                    processes.Add(RunProcess("mkvextract", $"\"{aFile}\" tracks {track.mMediaInfo.TrackNumber}:{supPaths.Back()}"));
                }

                processes.WaitAndClear();

                ////////////////////////////////////////////////////////////////
                // Convert sup files to SRT
                string pgsApp = "\"C:\\Program Files\\PgsToSrt\\PgsToSrt.dll\"";
                foreach (var supFile in supPaths)
                {
                    string srtFile = Path.ChangeExtension(supFile, ".srt").Replace("2_Intermediate", "3_ReadyToEdit");
                    processes.Add(RunProcess("dotnet", $"{pgsApp} --input {supFile} --output {srtFile} --tesseractlanguage eng"));
                }

                processes.WaitAndClear();
            }

            ////////////////////////////////////////////////////////////////
            // Audio
            {
                ////////////////////////////////////////////////////////////////
                // Extract out TrueHD track (id: 1 all files)
                var trueHdPaths = new List<string>();
                foreach (var track in new List<int> { 1, 3 })
                {
                    var trueHdPath = $"{intermediateFilePath}_Track_{track}.truehd";
                    processes.Add(RunProcess("mkvextract", $"\"{aFile}\" tracks {track}:{trueHdPath}"));
                    trueHdPaths.Add(trueHdPath);
                }

                processes.WaitAndClear();

                ////////////////////////////////////////////////////////////////
                // Convert TrueHD tracks to FLAC
                var flacPaths = new List<string>();
                foreach (var trueHdPath in trueHdPaths)
                {
                    var flacPath = Path.ChangeExtension(trueHdPath, ".flac").Replace("2_Intermediate", "3_ReadyToEdit");
                    processes.Add(RunProcess("ffmpeg", $"-analyzeduration 30000000 -acodec truehd -i {trueHdPath} -vn -sn -acodec flac {flacPath}"));
                }

                processes.WaitAndClear();
            }

            ////////////////////////////////////////////////////////////////
            // Video
            {
                ////////////////////////////////////////////////////////////////
                // Extract our video track (id: 0 all files)
                var mkvPath = $"{intermediateFilePath}_Track_0.mkv";
                RunProcess("mkvmerge", $"-o {mkvPath} --no-audio --no-subtitles {aFile}").WaitForExit();

                ////////////////////////////////////////////////////////////////
                // Convert MKV to MP4
                var mp4Path = $"{finalFilePath}_Track_0.mp4";
                RunProcess("ffmpeg", $"-i {mkvPath} -c copy {mp4Path}").WaitForExit();
            }

            Directory.Delete(intermediateEpisodeFolder, true);
        }

        static int EpisodeRange(int start, int end)
        {
            return end - start + 1;
        }

        static Dictionary<int, string> cNameRemapper = new Dictionary<int, string>
        {
            { 1,   "Black Clover - S01E0001 - Asta and Yuno.mkv"},
            { 2,   "Black Clover - S01E0002 - A Young Man's Vow.mkv"},
            { 3,   "Black Clover - S01E0003 - To the Royal Capital!.mkv"},
            { 4,   "Black Clover - S01E0004 - The Magic Knights Entrance Exam.mkv"},
            { 5,   "Black Clover - S01E0005 - The Road to the Wizard King.mkv"},
            { 6,   "Black Clover - S01E0006 - The Black Bulls.mkv"},
            { 7,   "Black Clover - S01E0007 - Another New Member.mkv"},
            { 8,   "Black Clover - S01E0008 - Go! Go! First Mission.mkv"},
            { 9,   "Black Clover - S01E0009 - Beasts.mkv"},
            { 10,  "Black Clover - S01E0010 - Guardians.mkv"},
            { 11,  "Black Clover - S01E0011 - What Happened on a Certain Day in the Castle Town.mkv"},
            { 12,  "Black Clover - S01E0012 - The Wizard King Saw.mkv"},
            { 13,  "Black Clover - S01E0013 - The Wizard King Saw, Continued!.mkv"},
            { 14,  "Black Clover - S01E0014 - Dungeon.mkv"},
            { 15,  "Black Clover - S01E0015 - The Diamond Mage.mkv"},
            { 16,  "Black Clover - S01E0016 - Friends.mkv"},
            { 17,  "Black Clover - S01E0017 - Destroyer.mkv"},
            { 18,  "Black Clover - S01E0018 - Memories of You.mkv"},
            { 19,  "Black Clover - S01E0019 - Destruction and Salvation.mkv"},
            { 20,  "Black Clover - S01E0020 - Assembly at the Royal Capital.mkv"},
            { 21,  "Black Clover - S01E0021 - Capital Riot.mkv"},
            { 22,  "Black Clover - S01E0022 - Wild Magic Dance.mkv"},
            { 23,  "Black Clover - S01E0023 - The King of the Crimson Lions.mkv"},
            { 24,  "Black Clover - S01E0024 - Blackout.mkv"},
            { 25,  "Black Clover - S01E0025 - Adversity.mkv"},
            { 26,  "Black Clover - S01E0026 - Wounded Beasts.mkv"},
            { 27,  "Black Clover - S01E0027 - Light.mkv"},
            { 28,  "Black Clover - S01E0028 - The One I've Set My Heart On.mkv"},
            { 29,  "Black Clover - S01E0029 - Path.mkv"},
            { 30,  "Black Clover - S01E0030 - The Mirror Mage.mkv"},
            { 31,  "Black Clover - S01E0031 - Pursuit Over the Snow.mkv"},
            { 32,  "Black Clover - S01E0032 - Three-Leaf Sprouts.mkv"},
            { 33,  "Black Clover - S01E0033 - To Help Somebody Someday.mkv"},
            { 34,  "Black Clover - S01E0034 - Light Magic vs. Dark Magic.mkv"},
            { 35,  "Black Clover - S01E0035 - The Light of Judgment.mkv"},
            { 36,  "Black Clover - S01E0036 - Three Eyes.mkv"},
            { 37,  "Black Clover - S01E0037 - The One With No Magic.mkv"},
            { 38,  "Black Clover - S01E0038 - The Magic Knights Captain Conference.mkv"},
            { 39,  "Black Clover - S01E0039 - Three-Leaf Salute.mkv"},
            { 40,  "Black Clover - S01E0040 - A Black Beach Story.mkv"},
            { 41,  "Black Clover - S01E0041 - The Water Girl Grows Up.mkv"},
            { 42,  "Black Clover - S01E0042 - The Underwater Temple.mkv"},
            { 43,  "Black Clover - S01E0043 - Temple Battle Royale.mkv"},
            { 44,  "Black Clover - S01E0044 - The Pointlessly Direct Fireball and the Wild Lightning.mkv"},
            { 45,  "Black Clover - S01E0045 - The Guy Who Doesn't Know When to Quit.mkv"},
            { 46,  "Black Clover - S01E0046 - Awakening.mkv"},
            { 47,  "Black Clover - S01E0047 - The Only Weapon.mkv"},
            { 48,  "Black Clover - S01E0048 - Despair vs. Hope.mkv"},
            { 49,  "Black Clover - S01E0049 - Beyond Limits.mkv"},
            { 50,  "Black Clover - S01E0050 - End of the Battle, End of Despair.mkv"},
            { 51,  "Black Clover - S01E0051 - Proof of Rightness.mkv"},
            { 52,  "Black Clover - S01E0052 - Whoever's Strongest Wins.mkv"},
            { 53,  "Black Clover - S01E0053 - Behind the Mask.mkv"},
            { 54,  "Black Clover - S01E0054 - Never Again.mkv"},
            { 55,  "Black Clover - S01E0055 - The Man Named Fanzell.mkv"},
            { 56,  "Black Clover - S01E0056 - The Man Named Fanzell, Continued.mkv"},
            { 57,  "Black Clover - S01E0057 - Infiltration.mkv"},
            { 58,  "Black Clover - S01E0058 - The Battlefield Decision.mkv"},
            { 59,  "Black Clover - S01E0059 - Flames of Hatred.mkv"},
            { 60,  "Black Clover - S01E0060 - Defectors' Atonement.mkv"},
            { 61,  "Black Clover - S01E0061 - The Promised World.mkv"},
            { 62,  "Black Clover - S01E0062 - Bettering One Another.mkv"},
            { 63,  "Black Clover - S01E0063 - Not in the Slightest.mkv"},
            { 64,  "Black Clover - S01E0064 - The Red Thread of Fate.mkv"},
            { 65,  "Black Clover - S01E0065 - I'm Home.mkv"},
            { 66,  "Black Clover - S01E0066 - The Secret of the Eye of the Midnight Sun.mkv"},
            { 67,  "Black Clover - S01E0067 - A Fun Festival Double Date.mkv"},
            { 68,  "Black Clover - S01E0068 - Battle to the Death! Yami vs. Jack.mkv"},
            { 69,  "Black Clover - S01E0069 - The Briar Maiden's Melancholy.mkv"},
            { 70,  "Black Clover - S01E0070 - Two New Stars.mkv"},
            { 71,  "Black Clover - S01E0071 - The Uncrowned, Undefeated Lioness.mkv"},
            { 72,  "Black Clover - S01E0072 - St. Elmo’s Fire.mkv"},
            { 73,  "Black Clover - S01E0073 - The Royal Knights Selection Test.mkv"},
            { 74,  "Black Clover - S01E0074 - Flower of Resolution.mkv"},
            { 75,  "Black Clover - S01E0075 - Fierce Battle.mkv"},
            { 76,  "Black Clover - S01E0076 - Mage X.mkv"},
            { 77,  "Black Clover - S01E0077 - Bad Blood.mkv"},
            { 78,  "Black Clover - S01E0078 - Peasant Trap.mkv"},
            { 79,  "Black Clover - S01E0079 - Mister Delinquent vs. Muscle Brains.mkv"},
            { 80,  "Black Clover - S01E0080 - Special Little Brother vs. Failed Big Brother.mkv"},
            { 81,  "Black Clover - S01E0081 - The Life of a Certain Man.mkv"},
            { 82,  "Black Clover - S01E0082 - Clover Clips! The Nightmarish Charmy Special!.mkv"},
            { 83,  "Black Clover - S01E0083 - Burn It into You.mkv"},
            { 84,  "Black Clover - S01E0084 - The Victors.mkv"},
            { 85,  "Black Clover - S01E0085 - Together in the Bath.mkv"},
            { 86,  "Black Clover - S01E0086 - Yami and Vangeance.mkv"},
            { 87,  "Black Clover - S01E0087 - Formation of the Royal Knights.mkv"},
            { 88,  "Black Clover - S01E0088 - Storming the Eye of the Midnight Sun's Hideout!!!.mkv"},
            { 89,  "Black Clover - S01E0089 - The Black Bulls' Hideout.mkv"},
            { 90,  "Black Clover - S01E0090 - Crazy Magic Battle.mkv"},
            { 91,  "Black Clover - S01E0091 - Mereoleona vs. Ryha the Disloyal.mkv"},
            { 92,  "Black Clover - S01E0092 - The Wizard King vs. The Leader of the Eye of the Midnight Sun.mkv"},
            { 93,  "Black Clover - S01E0093 - Julius Novachrono.mkv"},
            { 94,  "Black Clover - S01E0094 - New Future.mkv"},
            { 95,  "Black Clover - S01E0095 - Reincarnation.mkv"},
            { 96,  "Black Clover - S01E0096 - The Black Bulls Captain vs. the Crimson Wild Rose.mkv"},
            { 97,  "Black Clover - S01E0097 - Overwhelming Disadvantage.mkv"},
            { 98,  "Black Clover - S01E0098 - The Sleeping Lion.mkv"},
            { 99,  "Black Clover - S01E0099 - The Desperate Path Toward Survival.mkv"},
            { 100, "Black Clover - S01E0100 - We Won't Lose to You.mkv"},
            { 101, "Black Clover - S01E0101 - The Lives of the Village in the Sticks.mkv"},
            { 102, "Black Clover - S01E0102 - Two Miracles.mkv"},
            { 103, "Black Clover - S01E0103 - Release from Misfortune.mkv"},
            { 104, "Black Clover - S01E0104 - Lightning of Rage vs. Friends.mkv"},
            { 105, "Black Clover - S01E0105 - Smiles, Tears.mkv"},
            { 106, "Black Clover - S01E0106 - Path of Revenge, Path of Atonement.mkv"},
            { 107, "Black Clover - S01E0107 - The Battle for Clover Castle.mkv"},
            { 108, "Black Clover - S01E0108 - Battlefield Dancer.mkv"},
            { 109, "Black Clover - S01E0109 - Spatial Mage Brothers.mkv"},
            { 110, "Black Clover - S01E0110 - The Raging Bull Joins the Showdown!!.mkv"},
            { 111, "Black Clover - S01E0111 - The Eyes in the Mirror.mkv"},
            { 112, "Black Clover - S01E0112 - Humans Who Can Be Trusted.mkv"},
            { 113, "Black Clover - S01E0113 - Storming the Shadow Palace.mkv"},
            { 114, "Black Clover - S01E0114 - The Final Invaders.mkv"},
            { 115, "Black Clover - S01E0115 - Mastermind.mkv"},
            { 116, "Black Clover - S01E0116 - The Ultimate Natural Enemy.mkv"},
            { 117, "Black Clover - S01E0117 - Breaking the Seal.mkv"},
            { 118, "Black Clover - S01E0118 - A Reunion Across Time and Space.mkv"},
            { 119, "Black Clover - S01E0119 - The Final Attack.mkv"},
            { 120, "Black Clover - S01E0120 - Dawn.mkv"},
            { 121, "Black Clover - S01E0121 - Three Problems.mkv"},
            { 122, "Black Clover - S01E0122 - As Pitch Black as It Gets.mkv"},
            { 123, "Black Clover - S01E0123 - Nero Reminiscences... Part 1.mkv"},
            { 124, "Black Clover - S01E0124 - Nero Reminiscences... Part 2.mkv"},
            { 125, "Black Clover - S01E0125 - Return.mkv"},
            { 126, "Black Clover - S01E0126 - The Blue Rose's Confession.mkv"},
            { 127, "Black Clover - S01E0127 - Clues.mkv"},
            { 128, "Black Clover - S01E0128 - To the Heart Kingdom!.mkv"},
            { 129, "Black Clover - S01E0129 - Devil Megicula.mkv"},
            { 130, "Black Clover - S01E0130 - The New Magic Knight Captain Conference.mkv"},
            { 131, "Black Clover - S01E0131 - A New Resolve.mkv"},
            { 132, "Black Clover - S01E0132 - The Lion Awakens.mkv"},
            { 133, "Black Clover - S01E0133 - The Lion Awakens, Continued.mkv"},
            { 134, "Black Clover - S01E0134 - Those Who Have Been Gathered.mkv"},
            { 135, "Black Clover - S01E0135 - The One Who Has My Heart, My Mind, and Soul.mkv"},
            { 136, "Black Clover - S01E0136 - A Black Deep-Sea Story.mkv"},
            { 137, "Black Clover - S01E0137 - Charmy's Century of Hunger, Gordon's Millennium of Loneliness.mkv"},
            { 138, "Black Clover - S01E0138 - In Zara's Footsteps.mkv"},
            { 139, "Black Clover - S01E0139 - A Witch's Homecoming.mkv"},
        };

        static private List<string> GetFileList()
        {
            var minFileSize = ByteSize.FromGigaBytes(4);
            minFileSize.AddMegaBytes(500);
            //var minFileSize = ByteSize.FromGigaBytes(5);
            var maxFileSize = ByteSize.FromGigaBytes(9);

            var files = new List<string>();
            var diskEpisodes = new List<int> {
                EpisodeRange(1,6),
                EpisodeRange(7,10),
                EpisodeRange(11,17),
                EpisodeRange(18,19),
                EpisodeRange(20,25),
                EpisodeRange(26,29),
                EpisodeRange(30,35),
                EpisodeRange(36,39),
                EpisodeRange(40,46),
                EpisodeRange(47,51),
                EpisodeRange(52,58),
                EpisodeRange(59,63),
                EpisodeRange(64,69),
                EpisodeRange(70,72),
                EpisodeRange(73,78),
                EpisodeRange(79,83),
                EpisodeRange(84,89),
                EpisodeRange(90,90),
                EpisodeRange(91,97),
                EpisodeRange(98,102),
            };

            int i = 0;
            foreach (var directory in Directory
                .GetDirectories("D:\\Blurays")
                .Where(dir => Path.GetFileName(dir)
                .StartsWith("BlackClover"))
                .OrderBy(dir => dir))
            {
                var filesInDirectory = new List<string>();
                var underscoredDirectoryName = directory.Replace(' ', '_');
                if (!directory.Equals(underscoredDirectoryName))
                    Directory.Move(directory, underscoredDirectoryName);

                bool stopAdding = false;
                foreach (var file in Directory.GetFiles(underscoredDirectoryName).OrderBy(dir => dir))
                {
                    var underscoredFileName = file.Replace(' ', '_');
                    if (!file.Equals(underscoredFileName))
                        File.Move(file, underscoredFileName);

                    var info = new FileInfo(underscoredFileName);
                    var fileSize = ByteSize.FromBytes(info.Length);
                    if ((minFileSize < fileSize) && (fileSize < maxFileSize))
                    {
                        if (filesInDirectory.Back() != null)
                        {
                            var lastFile = Path.GetFileNameWithoutExtension(filesInDirectory.Back());
                            var currentFile = Path.GetFileNameWithoutExtension(underscoredFileName);
                            var lastTitleNumber = System.Int32.Parse(lastFile.Substring(lastFile.Count() - 2));
                            var titleNumber = System.Int32.Parse(currentFile.Substring(currentFile.Count() - 2));
                            
                            if (((lastTitleNumber + 1) == titleNumber) && !stopAdding)
                            {
                                filesInDirectory.Add(underscoredFileName);
                            }
                            else
                            {
                                stopAdding = true;
                            }
                        }
                        else
                        {
                            filesInDirectory.Add(underscoredFileName);
                        }
                    }
                }

                if (filesInDirectory.Count() != diskEpisodes[i++])
                    Debugger.Break();

                files.AddRange(filesInDirectory);
            }

            return files;
        }

        static List<string> GetFileListToProcess()
        {
            return Directory.GetFiles("D:\\BlackCloverFanEdit\\1_ToBeProcessed\\").OrderBy(file => file).ToList();
        }

        static void Main(string[] args)
        {
            //var files = GetFileList();
            //
            //Console.WriteLine($"File Count: {files.Count}");
            //Console.WriteLine($"Files");
            //
            //var changedNames = new List<string>();
            //
            //foreach (var episode in Enumerable.Range(1, files.Count()))
            //    changedNames.Add(files[episode - 1].Replace(Path.GetFileName(files[episode - 1]), cNameRemapper[episode]));
            //
            //var filesToProcess = new List<string>();
            //
            //foreach (var episode in Enumerable.Range(0, files.Count()))
            //{
            //    var normalizedFileName = Path.GetFileName(changedNames[episode]).Replace(" - ", "-").Replace(' ', '_');
            //    filesToProcess.Add(Path.Combine("D:\\BlackCloverFanEdit\\1_ToBeProcessed\\", normalizedFileName));
            //
            //    var copyingTo = Path.Combine("D:\\Kodi\\TV\\Black Clover", Path.GetFileName(changedNames[episode]));
            //    Console.WriteLine($"    Processing: {files[episode]}");
            //    Console.WriteLine($"        Copying file from {files[episode]} to {copyingTo}");
            //    Directory.CreateDirectory(Path.GetDirectoryName(copyingTo));
            //    Directory.CreateDirectory(Path.GetDirectoryName(filesToProcess.Back()));
            //    File.Copy(files[episode], copyingTo);
            //    
            //    Console.WriteLine($"        Moving file from {files[episode]} to {filesToProcess.Back()}");
            //    File.Move(files[episode], filesToProcess.Back());
            //}

            foreach (var file in GetFileListToProcess())
            {
                DtsToFlac(file);
            }
        }
    }
}
