using MelonLoader;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// Holds display and state metadata for an individual row/entry in the extended leaderboard UI.
    /// </summary>
    public class LeaderboardEntryData
    {
        public int Rank;
        public ulong SteamId;
        public string PlayerName;
        public int CountryId;
        public int CarId;
        public string CarName;
        public int Score;
        public string FormattedTime;
        public int EntryIndex;
        public bool IsSelected;
        public bool IsDownloading;
        public bool IsCached;
        public UGCHandle_t UgcHandle;
    }

    /// <summary>
    /// Manages Steam leaderboard requests, local cache checks, UGC replay downloads, and batch processing.
    /// </summary>
    public static class ReplayDownloader
    {
        public const int MAX_ALLOWED_GHOSTS = 333;

        public static bool isDownloadingBatch = false;
        public static List<LeaderboardEntryData> loadedEntries = new List<LeaderboardEntryData>();
        public static bool isFetchingEntries = false;

        private static CallResult<LeaderboardScoresDownloaded_t> onScoresDownloadedCallResult;

        // Static lookup array mapping internal car type IDs to descriptive names
        private static readonly string[] CarNamesMap = new string[]
        {
            "Rookie", "Shorty", "Baron", "Fox", "Petite", "Beast", "Dust Devil", "Thunderbolt",
            "Stylo", "Phantom", "Turbo", "Champion", "Fury", "La Princess", "Flow", "Contender",
            "Sandworm", "Classic", "Storm", "Kaiser", "Super", "Falcon", "Rocket", "Hurricane",
            "Crusher", "Nitro", "Phoenix", "Typhoon", "El Toro", "Roadslayer", "Dakar", "Chaser",
            "Big Pride", "Fire Fighter", "Soap Spinner", "Godspeed", "Crawler FWD", "Hellcat",
            "Panther", "Star Kart", "Mr. President", "Antivirus", "Oldschool", "Gorilla", "Horizon",
            "The Rock", "Colossus", "Majesty", "Delirium", "Safari", "Unrivaled RWD", "Hallowheel"
        };

        /// <summary>
        /// Requests a specific range of entries from the active Steam leaderboard.
        /// </summary>
        public static void FetchLeaderboardRange(int rangeStart, int rangeEnd)
        {
            if (isFetchingEntries) return;

            var steamFuncs = GameManager.game_manager?.steam_functions;
            if (steamFuncs == null)
            {
                MelonLogger.Warning("[Mod] SteamFunctions instance is null.");
                return;
            }

            FieldInfo handleField = typeof(SteamFunctions).GetField("currentlySelectedLeaderboard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handleField == null) return;

            SteamLeaderboard_t handle = (SteamLeaderboard_t)handleField.GetValue(steamFuncs);
            if (handle.m_SteamLeaderboard == 0)
            {
                steamFuncs.RequestWorldRecordLeaderboard(TrackManager.current_track_id, false);
                return;
            }

            isFetchingEntries = true;
            MainMod.statusMessage = $"Fetching #{rangeStart}..#{rangeEnd}...";

            SteamAPICall_t call = SteamUserStats.DownloadLeaderboardEntries(handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal, rangeStart, rangeEnd);

            if (onScoresDownloadedCallResult == null)
            {
                onScoresDownloadedCallResult = CallResult<LeaderboardScoresDownloaded_t>.Create(OnScoresDownloaded);
            }

            onScoresDownloadedCallResult.Set(call);
        }

        /// <summary>
        /// Steam API callback triggered when leaderboard score download completes. Parses and populates table entries.
        /// </summary>
        private static void OnScoresDownloaded(LeaderboardScoresDownloaded_t pCallback, bool bIOFailure)
        {
            isFetchingEntries = false;

            if (bIOFailure || pCallback.m_cEntryCount == 0)
            {
                MainMod.statusMessage = bIOFailure ? "Steam IO Error" : "No entries found";
                return;
            }

            int count = pCallback.m_cEntryCount;
            SteamLeaderboardEntries_t hEntries = pCallback.m_hSteamLeaderboardEntries;
            int trackId = TrackManager.current_track_id;

            for (int i = 0; i < count; i++)
            {
                int[] details = new int[3];
                LeaderboardEntry_t entry;

                if (SteamUserStats.GetDownloadedLeaderboardEntry(hEntries, i, out entry, details, details.Length))
                {
                    int carType = details[0];
                    int countryId = details[2];
                    float timeSeconds = (float)entry.m_nScore / 1000f;
                    ulong steamId = entry.m_steamIDUser.m_SteamID;
                    int scoreMs = entry.m_nScore;

                    ReplayCacheManager.InvalidateOutdatedCache(trackId, steamId, scoreMs);
                    bool cached = ReplayCacheManager.IsReplayCached(trackId, steamId, scoreMs);

                    LeaderboardEntryData data = new LeaderboardEntryData
                    {
                        Rank = entry.m_nGlobalRank,
                        SteamId = steamId,
                        PlayerName = SteamFriends.GetFriendPersonaName(entry.m_steamIDUser),
                        CountryId = countryId,
                        CarId = carType,
                        CarName = GetCarNameByID(carType),
                        Score = scoreMs,
                        FormattedTime = NotificationManager.FormatTime(timeSeconds),
                        EntryIndex = loadedEntries.Count,
                        IsSelected = false,
                        IsDownloading = false,
                        IsCached = cached,
                        UgcHandle = entry.m_hUGC
                    };

                    loadedEntries.Add(data);
                }
            }

            MainMod.statusMessage = "Ready";
        }

        /// <summary>
        /// Resolves the car name string from the given car type ID.
        /// </summary>
        private static string GetCarNameByID(int carType)
        {
            if (carType >= 0 && carType < CarNamesMap.Length)
            {
                return CarNamesMap[carType];
            }
            return $"Car #{carType}";
        }

        /// <summary>
        /// Coroutine that iterates through selected entries, loads from cache or downloads via UGC, queues them, and triggers replay playback.
        /// </summary>
        public static IEnumerator ViewReplayWithSelectedEntries(SteamFunctions steamFuncs)
        {
            isDownloadingBatch = true;
            MainMod.queuedLeaderboardReplays.Clear();
            int trackID = TrackManager.current_track_id;
            int loadedCount = 0;
            ReplayData lastLoadedReplay = null;
            int lastRank = 1;
            int lastEntryIndex = 0;

            FieldInfo handleField = typeof(SteamFunctions).GetField("currentlySelectedLeaderboard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            SteamLeaderboard_t leaderboardHandle = handleField != null ? (SteamLeaderboard_t)handleField.GetValue(steamFuncs) : default;

            for (int i = 0; i < loadedEntries.Count; i++)
            {
                if (loadedCount >= MAX_ALLOWED_GHOSTS)
                {
                    MelonLogger.Msg($"[Mod] Maximum ghost limit ({MAX_ALLOWED_GHOSTS}) reached.");
                    break;
                }

                var entry = loadedEntries[i];
                if (!entry.IsSelected) continue;

                // Respect active country filter if set
                if (LeaderboardUI.selectedCountryFilter != -1 && entry.CountryId != LeaderboardUI.selectedCountryFilter) continue;

                lastRank = entry.Rank;
                lastEntryIndex = entry.EntryIndex;

                // Step 1: Check local JSON cache first
                ReplayData cachedData = ReplayCacheManager.LoadReplayFromCache(trackID, entry.SteamId, entry.Score);
                if (cachedData != null)
                {
                    entry.IsCached = true;
                    MainMod.queuedLeaderboardReplays.Add(cachedData);
                    lastLoadedReplay = cachedData;
                    loadedCount++;
                    MelonLogger.Msg($"[Mod] Replay #{entry.Rank} ({entry.PlayerName}) loaded from local cache.");
                    continue;
                }

                // Step 2: Download via Steam UGC if not cached
                entry.IsDownloading = true;
                MainMod.statusMessage = $"Downloading #{entry.Rank}...";

                ClearDownloadedUGCBuffer();
                SyncNativeLeaderboardSelection(steamFuncs, entry.EntryIndex);

                if (entry.UgcHandle.m_UGCHandle != 0 && leaderboardHandle.m_SteamLeaderboard != 0)
                {
                    steamFuncs.DownloadUGC(entry.UgcHandle, leaderboardHandle, entry.EntryIndex);
                }
                else
                {
                    steamFuncs.DownloadUGCForSelectedLeaderboardEntry();
                }

                float timeout = 4.0f;
                ReplayData downloaded = null;

                while (timeout > 0f)
                {
                    yield return new WaitForSeconds(0.15f);
                    downloaded = SteamFunctions.GetDownloadedUGCReplay(trackID);
                    if (downloaded != null) break;
                    timeout -= 0.15f;
                }

                entry.IsDownloading = false;

                if (downloaded != null)
                {
                    ReplayCacheManager.SaveReplayToCache(trackID, entry.SteamId, entry.Score, downloaded);
                    entry.IsCached = true;

                    MainMod.queuedLeaderboardReplays.Add(downloaded);
                    lastLoadedReplay = downloaded;
                    loadedCount++;
                    MelonLogger.Msg($"[Mod] Replay #{entry.Rank} ({downloaded.playername}) downloaded & cached.");
                }
                else
                {
                    MelonLogger.Warning($"[Mod] Replay #{entry.Rank} download timed out.");
                }
            }

            if (MainMod.queuedLeaderboardReplays.Count > 0)
            {
                // Inject the final selected replay into native SteamFunctions buffers
                if (lastLoadedReplay != null)
                {
                    SetDownloadedUGCReplayBuffer(lastLoadedReplay);
                    SetFieldByName(steamFuncs, "downloadedUGCReplayRankFromLBSelect", lastRank);
                    SyncNativeLeaderboardSelection(steamFuncs, lastEntryIndex);
                }

                MainMod.statusMessage = $"{MainMod.queuedLeaderboardReplays.Count} Replays Ready!";
                isDownloadingBatch = false;
                GameManager.game_manager.MainMenuButtonPressed(5);
            }
            else
            {
                MainMod.statusMessage = "No replays loaded!";
                isDownloadingBatch = false;
            }
        }

        /// <summary>
        /// Coroutine that pre-caches selected entries without viewing them immediately.
        /// </summary>
        public static IEnumerator LoadSelectedEntriesOnly(SteamFunctions steamFuncs)
        {
            isDownloadingBatch = true;
            int trackID = TrackManager.current_track_id;
            int savedCount = 0;

            FieldInfo handleField = typeof(SteamFunctions).GetField("currentlySelectedLeaderboard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            SteamLeaderboard_t leaderboardHandle = handleField != null ? (SteamLeaderboard_t)handleField.GetValue(steamFuncs) : default;

            for (int i = 0; i < loadedEntries.Count; i++)
            {
                var entry = loadedEntries[i];
                if (!entry.IsSelected || entry.IsCached) continue;

                entry.IsDownloading = true;
                MainMod.statusMessage = $"Caching #{entry.Rank}...";

                ClearDownloadedUGCBuffer();
                SyncNativeLeaderboardSelection(steamFuncs, entry.EntryIndex);

                if (entry.UgcHandle.m_UGCHandle != 0 && leaderboardHandle.m_SteamLeaderboard != 0)
                {
                    steamFuncs.DownloadUGC(entry.UgcHandle, leaderboardHandle, entry.EntryIndex);
                }
                else
                {
                    steamFuncs.DownloadUGCForSelectedLeaderboardEntry();
                }

                float timeout = 4.0f;
                ReplayData downloaded = null;

                while (timeout > 0f)
                {
                    yield return new WaitForSeconds(0.15f);
                    downloaded = SteamFunctions.GetDownloadedUGCReplay(trackID);
                    if (downloaded != null) break;
                    timeout -= 0.15f;
                }

                entry.IsDownloading = false;

                if (downloaded != null)
                {
                    ReplayCacheManager.SaveReplayToCache(trackID, entry.SteamId, entry.Score, downloaded);
                    entry.IsCached = true;
                    savedCount++;
                }
                else
                {
                    MelonLogger.Warning($"[Mod] Replay #{entry.Rank} download timed out.");
                }
            }

            MainMod.statusMessage = savedCount > 0 ? $"{savedCount} Replays Cached!" : "All selected already cached";
            isDownloadingBatch = false;
        }

        /// <summary>
        /// Syncs native game reflection fields for leaderboard entry selection tracking.
        /// </summary>
        private static void SyncNativeLeaderboardSelection(SteamFunctions steamFuncs, int entryIndex)
        {
            SetFieldByName(steamFuncs, "selectedLeaderboardEntry", entryIndex);
            SetFieldByName(steamFuncs, "selectedLeaderboardEntryWhenDLStarted", entryIndex);
        }

        private static void SetDownloadedUGCReplayBuffer(ReplayData replay)
        {
            SetFieldByName(GameManager.game_manager?.steam_functions, "downloadedUGCReplay", replay);
        }

        public static void ClearDownloadedUGCBuffer()
        {
            SetFieldByName(GameManager.game_manager?.steam_functions, "downloadedUGCReplay", null);
        }

        /// <summary>
        /// Generic reflection helper to set private or public fields by name.
        /// </summary>
        private static void SetFieldByName(object target, string fieldName, object value)
        {
            if (target == null) return;
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (field != null)
            {
                if (field.IsStatic)
                    field.SetValue(null, value);
                else
                    field.SetValue(target, value);
            }
        }

        /// <summary>
        /// Clears all loaded entries and resets downloader state.
        /// </summary>
        public static void ResetTableData()
        {
            loadedEntries.Clear();
            isFetchingEntries = false;
            isDownloadingBatch = false;
            MainMod.statusMessage = "Ready";
            LeaderboardUI.showExtendedWindow = false;
        }
    }
}