using MelonLoader;
using Steamworks;
using System;
using System.Reflection;
using UnityEngine;

namespace MultiGhostReplays
{
    public static class LeaderboardUI
    {
        public static bool showExtendedWindow = false;
        private static Vector2 scrollPosition = Vector2.zero;

        // Country filter and picker control variables
        private static bool showCountryPicker = false;
        private static Vector2 countryScrollPosition = Vector2.zero;
        public static int selectedCountryFilter = -1; // -1 means "Global" (no filter)

        // Track change detection and loading completion flags
        private static int lastTrackIdChecked = -1;
        private static bool allPlayersLoadedForCurrentTrack = false;

        // Solid background texture for modal opaqueness
        private static Texture2D solidBackgroundTex = null;

        private static Texture2D GetSolidBackgroundTexture()
        {
            if (solidBackgroundTex == null)
            {
                solidBackgroundTex = new Texture2D(1, 1);
                solidBackgroundTex.SetPixel(0, 0, new Color(28f / 255f, 28f / 255f, 31f / 255f, 1f));
                solidBackgroundTex.Apply();
            }
            return solidBackgroundTex;
        }

        public static void DrawLeaderboardCheckboxes()
        {
            var gameMgr = GameManager.game_manager;
            if (gameMgr == null || gameMgr.steam_functions == null) return;

            // Security check: Only display the leaderboard button during valid race/track menus
            int menuState = gameMgr.menu_state;
            bool isValidMenuState =
                menuState == GameManager.MENU_STATE_TRACK_SELECTION_QUICK_RACE ||
                menuState == GameManager.MENU_STATE_QUICK_RACE_END_INFORMATION ||
                menuState == GameManager.MENU_STATE_TRACK_INFORMATION_WORLD ||
                menuState == GameManager.MENU_STATE_QUICK_RACE_IN_PROGRESS ||
                menuState == GameManager.MENU_STATE_VIEW_REPLAY_QUICK_RACE ||
                menuState == GameManager.MENU_STATE_VIEW_REPLAY_WORLD;

            if (!isValidMenuState)
            {
                showExtendedWindow = false;
                showCountryPicker = false;
                return;
            }

            float scaleX = Screen.width / 1920f;
            float scaleY = Screen.height / 1080f;

            float btnWidth = (float)Math.Round(140f * scaleX);
            float btnHeight = (float)Math.Round(45f * scaleY);
            float btnX = Screen.width - (float)Math.Round(465f * scaleX) - btnWidth;
            float btnY = Screen.height - (float)Math.Round(445f * scaleY);

            GUIStyle fullTableBtnStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = (int)Math.Round(14f * scaleX),
                fontStyle = FontStyle.Bold
            };

            if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), "LEADERBOARD", fullTableBtnStyle))
            {
                showExtendedWindow = !showExtendedWindow;

                if (showExtendedWindow && ReplayDownloader.loadedEntries.Count == 0)
                {
                    ReplayDownloader.FetchLeaderboardRange(1, 50);
                }
            }

            if (showExtendedWindow)
            {
                DrawExtendedLeaderboardWindow(gameMgr.steam_functions);
            }
        }

        private static void DrawExtendedLeaderboardWindow(SteamFunctions steamFuncs)
        {
            float winWidth = 610f;
            float winHeight = Screen.height / 2f;
            float winX = (Screen.width - winWidth) / 2f;
            float winY = (Screen.height - winHeight) / 2f;

            // Validate track change to reset the "all players loaded" state flag
            int currentTrackId = TrackManager.current_track_id;
            if (lastTrackIdChecked != currentTrackId)
            {
                lastTrackIdChecked = currentTrackId;
                allPlayersLoadedForCurrentTrack = false;
            }

            // Window frames
            GUI.Box(new Rect(winX, winY, winWidth, winHeight), "");
            GUI.Box(new Rect(winX + 2, winY + 2, winWidth - 4, winHeight - 4), "Leaderboard Replay Browser");

            // Temporarily disable clicks on leaderboard controls if the country picker is open
            GUI.enabled = !showCountryPicker;

            // Close button
            if (GUI.Button(new Rect(winX + winWidth - 30f, winY + 5f, 24f, 22f), "X"))
            {
                showExtendedWindow = false;
                showCountryPicker = false;
            }

            // Filter entries based on the active country filter early for accurate selection counting
            var filteredEntries = ReplayDownloader.loadedEntries.FindAll(e => selectedCountryFilter == -1 || e.CountryId == selectedCountryFilter);

            // Audio overflow warning (> 166 selected entries in the current filtered view)
            int selectedCount = filteredEntries.FindAll(e => e.IsSelected).Count;
            bool shouldShowTooltip = false;

            if (selectedCount > 166)
            {
                Rect warningRect = new Rect(winX + winWidth - 170f, winY + 32f, 160f, 24f);

                // 1. Glow effect style (translucent red halo)
                GUIStyle glowStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = 11
                };
                glowStyle.normal.textColor = new Color(1f, 0f, 0f, 0.5f);

                // Render offset text in 4 directions to form the glow halo
                GUI.Label(new Rect(warningRect.x - 1f, warningRect.y, warningRect.width, warningRect.height), "[!] AUDIO OVERFLOW", glowStyle);
                GUI.Label(new Rect(warningRect.x + 1f, warningRect.y, warningRect.width, warningRect.height), "[!] AUDIO OVERFLOW", glowStyle);
                GUI.Label(new Rect(warningRect.x, warningRect.y - 1f, warningRect.width, warningRect.height), "[!] AUDIO OVERFLOW", glowStyle);
                GUI.Label(new Rect(warningRect.x, warningRect.y + 1f, warningRect.width, warningRect.height), "[!] AUDIO OVERFLOW", glowStyle);

                // 2. Main text layer rendered crisp on top with custom light-red shade
                GUIStyle mainStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = 11
                };
                mainStyle.normal.textColor = new Color(1f, 0.32f, 0.32f, 1f);
                GUI.Label(warningRect, "[!] AUDIO OVERFLOW", mainStyle);

                if (warningRect.Contains(Event.current.mousePosition))
                {
                    shouldShowTooltip = true;
                }
            }

            // Selection controls (Check / Uncheck filtered items)
            if (GUI.Button(new Rect(winX + 15f, winY + 30f, 30f, 24f), "✓"))
            {
                foreach (var entry in ReplayDownloader.loadedEntries)
                {
                    if (selectedCountryFilter == -1 || entry.CountryId == selectedCountryFilter)
                        entry.IsSelected = true;
                }
            }

            if (GUI.Button(new Rect(winX + 50f, winY + 30f, 30f, 24f), "X"))
            {
                foreach (var entry in ReplayDownloader.loadedEntries)
                {
                    if (selectedCountryFilter == -1 || entry.CountryId == selectedCountryFilter)
                        entry.IsSelected = false;
                }
            }

            // Fixed "G" reset button or reserved empty space slot
            Rect gButtonRect = new Rect(winX + 85f, winY + 30f, 30f, 24f);
            if (selectedCountryFilter != -1)
            {
                if (GUI.Button(gButtonRect, "G"))
                {
                    selectedCountryFilter = -1;
                }
            }

            // Action buttons with static X positioning
            float actionButtonsStartX = winX + 120f;
            if (!ReplayDownloader.isDownloadingBatch)
            {
                if (GUI.Button(new Rect(actionButtonsStartX, winY + 30f, 180f, 24f), "VIEW WITH SELECTED"))
                {
                    MelonCoroutines.Start(ReplayDownloader.ViewReplayWithSelectedEntries(steamFuncs));
                }

                if (GUI.Button(new Rect(actionButtonsStartX + 190f, winY + 30f, 120f, 24f), "LOAD SELECTED"))
                {
                    MelonCoroutines.Start(ReplayDownloader.LoadSelectedEntriesOnly(steamFuncs));
                }
            }

            // Table headers layout
            float headerY = winY + 62f;
            GUI.Box(new Rect(winX + 10f, headerY, winWidth - 35f, 26f), "");
            GUI.Label(new Rect(winX + 21f, headerY + 3f, 35f, 20f), "Set");
            GUI.Label(new Rect(winX + 51f, headerY + 3f, 25f, 20f), "Chk");
            GUI.Label(new Rect(winX + 85f, headerY + 3f, 45f, 20f), "Rank");

            // Flag column header button remains enabled to allow toggling the country picker
            GUI.enabled = true;
            GUIStyle flagHeaderButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            if (GUI.Button(new Rect(winX + 128f, headerY + 3f, 39f, 20f), "Flag", flagHeaderButtonStyle))
            {
                showCountryPicker = !showCountryPicker;
            }

            GUI.Label(new Rect(winX + 180f, headerY + 3f, 180f, 20f), "Player Name");
            GUI.Label(new Rect(winX + 370f, headerY + 3f, 120f, 20f), "Car Model");
            GUI.Label(new Rect(winX + 500f, headerY + 3f, 120f, 20f), "Time");

            // Restore blocking state for table rows
            GUI.enabled = !showCountryPicker;

            // Scroll View Area (Filtered list) with optimized virtualization
            float tableY = winY + 92f;
            float tableHeight = winHeight - 145f;

            float rowHeight = 28f;
            float contentHeight = filteredEntries.Count * rowHeight;

            scrollPosition = GUI.BeginScrollView(
                new Rect(winX + 10f, tableY, winWidth - 20f, tableHeight),
                scrollPosition,
                new Rect(0f, 0f, winWidth - 40f, contentHeight),
                false, false
            );

            Color defaultGuiColor = GUI.color;

            GUIStyle rangeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);

            int totalVisibleRows = Mathf.CeilToInt(tableHeight / rowHeight) + 2;
            int startIndex = Mathf.FloorToInt(scrollPosition.y / rowHeight);
            startIndex = Mathf.Max(0, startIndex - 2);
            int endIndex = Mathf.Min(filteredEntries.Count, startIndex + totalVisibleRows + 4);

            for (int i = startIndex; i < endIndex; i++)
            {
                var entry = filteredEntries[i];
                float rowY = i * rowHeight;

                if (entry.IsDownloading)
                {
                    GUI.color = Color.red;
                    GUI.Box(new Rect(0f, rowY, winWidth - 40f, rowHeight - 2f), "");
                    GUI.color = defaultGuiColor;
                }

                if (GUI.Button(new Rect(5f, rowY + 2f, 32f, 22f), "", rangeButtonStyle))
                {
                    ToggleRangeSelectionUpTo(filteredEntries, i);
                }

                entry.IsSelected = GUI.Toggle(new Rect(45f, rowY + 4f, 20f, 20f), entry.IsSelected, "");

                labelStyle.normal.textColor = entry.IsCached ? Color.yellow : Color.white;

                GUI.Label(new Rect(75f, rowY + 3f, 45f, 20f), $"#{entry.Rank}", labelStyle);

                if (entry.CountryId >= 0 && GameManager.game_manager != null && entry.CountryId < GameManager.game_manager.countryFlags.Length)
                {
                    Texture flagTex = (Texture)GameManager.game_manager.countryFlags[entry.CountryId];
                    if (flagTex != null)
                    {
                        Rect flagDrawRect = new Rect(122f, rowY + 4f, 31f, 19f);
                        GUI.DrawTexture(flagDrawRect, flagTex);

                        if (GUI.enabled && Event.current.type == EventType.MouseDown && flagDrawRect.Contains(Event.current.mousePosition))
                        {
                            selectedCountryFilter = entry.CountryId;
                            Event.current.Use();
                        }
                    }
                }

                GUI.Label(new Rect(170f, rowY + 3f, 180f, 20f), entry.PlayerName, labelStyle);
                GUI.Label(new Rect(360f, rowY + 3f, 120f, 20f), entry.CarName, labelStyle);
                GUI.Label(new Rect(490f, rowY + 3f, 120f, 20f), entry.FormattedTime, labelStyle);
            }

            GUI.EndScrollView();

            // Check if loaded entries reached the maximum Steam leaderboard entry count
            var steamFuncsInstance = GameManager.game_manager?.steam_functions;
            if (steamFuncsInstance != null)
            {
                FieldInfo handleField = typeof(SteamFunctions).GetField("currentlySelectedLeaderboard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (handleField != null)
                {
                    SteamLeaderboard_t lbHandle = (SteamLeaderboard_t)handleField.GetValue(steamFuncsInstance);
                    if (lbHandle.m_SteamLeaderboard != 0)
                    {
                        int totalSteamEntries = SteamUserStats.GetLeaderboardEntryCount(lbHandle);
                        if (totalSteamEntries > 0 && ReplayDownloader.loadedEntries.Count >= totalSteamEntries)
                        {
                            allPlayersLoadedForCurrentTrack = true;
                        }
                    }
                }
            }

            // Apply block state to bottom action buttons if the country picker is open
            if (showCountryPicker)
            {
                GUI.enabled = false;
            }
            else
            {
                GUI.enabled = !allPlayersLoadedForCurrentTrack;
            }

            // Pagination batch fetch buttons (50, 100, 200 and 500) at the bottom footer
            float bottomY = winY + winHeight - 42f;
            if (!ReplayDownloader.isFetchingEntries)
            {
                float btnW = 95f;
                float spacing = 6f;
                float totalW = (btnW * 4f) + (spacing * 3f);
                float startX = winX + (winWidth - totalW) / 2f;

                if (GUI.Button(new Rect(startX, bottomY, btnW, 30f), "Add 50"))
                {
                    int nextStart = ReplayDownloader.loadedEntries.Count + 1;
                    ReplayDownloader.FetchLeaderboardRange(nextStart, nextStart + 49);
                }

                if (GUI.Button(new Rect(startX + btnW + spacing, bottomY, btnW, 30f), "Add 100"))
                {
                    int nextStart = ReplayDownloader.loadedEntries.Count + 1;
                    ReplayDownloader.FetchLeaderboardRange(nextStart, nextStart + 99);
                }

                if (GUI.Button(new Rect(startX + (btnW + spacing) * 2f, bottomY, btnW, 30f), "Add 200"))
                {
                    int nextStart = ReplayDownloader.loadedEntries.Count + 1;
                    ReplayDownloader.FetchLeaderboardRange(nextStart, nextStart + 199);
                }

                if (GUI.Button(new Rect(startX + (btnW + spacing) * 3f, bottomY, btnW, 30f), "Add 500"))
                {
                    int nextStart = ReplayDownloader.loadedEntries.Count + 1;
                    ReplayDownloader.FetchLeaderboardRange(nextStart, nextStart + 499);
                }
            }

            // Restore GUI.enabled for modal rendering
            GUI.enabled = true;

            // Country Selector Modal Overlay Panel
            if (showCountryPicker)
            {
                float pickerWidth = 536f;
                float pickerHeight = 370f;
                float pickerX = winX + (winWidth - pickerWidth) / 2f;
                float pickerY = winY + (winHeight - pickerHeight) / 2f;
                Rect pickerRect = new Rect(pickerX, pickerY, pickerWidth, pickerHeight);

                // Close country picker on outside click
                if (Event.current.type == EventType.MouseDown && !pickerRect.Contains(Event.current.mousePosition))
                {
                    showCountryPicker = false;
                    Event.current.Use();
                }

                // Custom box style with solid background and forced white text color
                GUIStyle opaqueBoxStyle = new GUIStyle(GUI.skin.box);
                opaqueBoxStyle.normal.background = GetSolidBackgroundTexture();
                opaqueBoxStyle.active.background = GetSolidBackgroundTexture();
                opaqueBoxStyle.hover.background = GetSolidBackgroundTexture();
                opaqueBoxStyle.focused.background = GetSolidBackgroundTexture();

                opaqueBoxStyle.normal.textColor = Color.white;
                opaqueBoxStyle.hover.textColor = Color.white;
                opaqueBoxStyle.active.textColor = Color.white;
                opaqueBoxStyle.focused.textColor = Color.white;

                GUI.Box(pickerRect, "", opaqueBoxStyle);

                GUI.BeginGroup(pickerRect);
                {
                    GUI.Box(new Rect(2f, 2f, pickerWidth - 4f, pickerHeight - 4f), "Select Country Filter", opaqueBoxStyle);

                    if (GUI.Button(new Rect(pickerWidth - 30f, 5f, 24f, 22f), "X"))
                    {
                        showCountryPicker = false;
                    }

                    Rect innerAreaRect = new Rect(10f, 35f, pickerWidth - 20f, pickerHeight - 45f);

                    // Restrict mouse scroll wheel strictly to the country picker container
                    if (Event.current.type == EventType.ScrollWheel)
                    {
                        if (innerAreaRect.Contains(Event.current.mousePosition))
                        {
                            countryScrollPosition.y += Event.current.delta.y * 15f;
                            Event.current.Use();
                        }
                        else
                        {
                            Event.current.Use();
                        }
                    }

                    GUILayout.BeginArea(innerAreaRect);
                    {
                        GUILayout.BeginVertical();
                        GUILayout.Space(5f);

                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Global", GUILayout.Width(150f), GUILayout.Height(30f)))
                        {
                            selectedCountryFilter = -1;
                            showCountryPicker = false;
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.Space(5f);

                        // Country flags grid layout (10 columns, 50px cell width)
                        countryScrollPosition = GUILayout.BeginScrollView(countryScrollPosition, GUILayout.Width(pickerWidth - 20f), GUILayout.Height(pickerHeight - 98f));

                        if (GameManager.game_manager != null && GameManager.game_manager.countryFlags != null)
                        {
                            var flags = GameManager.game_manager.countryFlags;
                            int columns = 10;
                            int totalFlags = flags.Length;
                            int rows = Mathf.CeilToInt((float)totalFlags / columns);

                            for (int r = 0; r < rows; r++)
                            {
                                GUILayout.BeginHorizontal();
                                for (int c = 0; c < columns; c++)
                                {
                                    int index = r * columns + c;
                                    if (index < totalFlags)
                                    {
                                        Texture flagTex = (Texture)flags[index];
                                        if (flagTex != null)
                                        {
                                            GUILayout.BeginVertical(GUILayout.Width(50f), GUILayout.Height(29f));
                                            {
                                                GUILayout.BeginHorizontal();
                                                GUILayout.FlexibleSpace();

                                                Rect flagBtnRect = GUILayoutUtility.GetRect(36f, 22f, GUILayout.Width(36f), GUILayout.Height(22f));
                                                GUI.DrawTexture(flagBtnRect, flagTex);

                                                if (Event.current.type == EventType.MouseDown && flagBtnRect.Contains(Event.current.mousePosition))
                                                {
                                                    selectedCountryFilter = index;
                                                    showCountryPicker = false;
                                                    Event.current.Use();
                                                }

                                                GUILayout.FlexibleSpace();
                                                GUILayout.EndHorizontal();
                                            }
                                            GUILayout.EndVertical();
                                        }
                                        else
                                        {
                                            GUILayout.Space(50f);
                                        }
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }
                        }

                        GUILayout.EndScrollView();
                        GUILayout.EndVertical();
                    }
                    GUILayout.EndArea();
                }
                GUI.EndGroup();
            }

            // Draw Tooltip in a fixed box TO THE RIGHT of the main Leaderboard Window
            if (shouldShowTooltip)
            {
                float tooltipWidth = 220f;
                float tooltipHeight = 110f;
                float tooltipX = winX + winWidth + 10f;
                float tooltipY = winY + 30f;

                Rect tooltipBoxRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);

                GUI.Box(tooltipBoxRect, "");
                GUIStyle tooltipTextStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter
                };
                tooltipTextStyle.normal.textColor = Color.yellow;

                GUI.Label(new Rect(tooltipBoxRect.x + 8f, tooltipBoxRect.y + 8f, tooltipBoxRect.width - 16f, tooltipBoxRect.height - 16f),
                    "Selecting more than 166 ghost cars exceeds Unity's audio engine channel limits. Engine sound effects may stutter, glitch, or turn completely silent.", tooltipTextStyle);
            }
        }

        private static void ToggleRangeSelectionUpTo(System.Collections.Generic.List<LeaderboardEntryData> list, int targetIndex)
        {
            bool allChecked = true;

            for (int i = 0; i <= targetIndex; i++)
            {
                if (i < list.Count && !list[i].IsSelected)
                {
                    allChecked = false;
                    break;
                }
            }

            bool newState = !allChecked;

            for (int i = 0; i <= targetIndex; i++)
            {
                if (i < list.Count)
                {
                    list[i].IsSelected = newState;
                }
            }
        }
    }
}