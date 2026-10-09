using System;
using System.Collections;
using UnityEngine;
using MelonLoader;

namespace MultiGhostReplays
{
    /// <summary>
    /// Handles OnGUI rendering for the leaderboard selection overlay and replay control windows.
    /// </summary>
    public static class LeaderboardUI
    {
        public static bool[] selectedEntries = new bool[10];

        /// <summary>
        /// Draws the quick top-10 leaderboard checkbox overlay on track select screens.
        /// </summary>
        public static void DrawLeaderboardCheckboxes()
        {
            var steamFuncs = GameManager.game_manager.steam_functions;
            if (steamFuncs == null) return;

            IList entries = ReplayDownloader.GetLeaderboardEntriesList(steamFuncs);
            if (entries == null) return;

            float scaleX = Screen.width / 1920f;
            float scaleY = Screen.height / 1080f;

            float panelWidth = (float)Math.Round(128f * scaleX);
            float panelX = Screen.width - (float)Math.Round(465f * scaleX) - panelWidth;
            float panelY = Screen.height - (float)Math.Round(445f * scaleY);
            float panelHeight = (float)Math.Round(439f * scaleY);

            GUI.Box(new Rect(panelX, panelY, panelWidth, panelHeight), "");

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = (int)Math.Round(16f * scaleX);
            titleStyle.normal.textColor = Color.white;

            float titleOffsetX = (float)Math.Round(12f * scaleX);
            float titleOffsetY = (float)Math.Round(8f * scaleY);
            float titleWidth = panelWidth - ((float)Math.Round(24f * scaleX));
            float titleHeight = (float)Math.Round(38f * scaleY);

            GUI.Label(new Rect(panelX + titleOffsetX, panelY + titleOffsetY, titleWidth, titleHeight), "Replay\nSelection", titleStyle);

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.fontSize = (int)Math.Round(14f * scaleX);

            float btnOffsetY = (float)Math.Round(54f * scaleY);
            float btnHeight = (float)Math.Round(42f * scaleY);

            if (!ReplayDownloader.isDownloadingBatch && GUI.Button(new Rect(panelX + titleOffsetX, panelY + btnOffsetY, titleWidth, btnHeight), "LOAD\nSELECTED", buttonStyle))
            {
                MelonCoroutines.Start(ReplayDownloader.DownloadAndLaunchSelectedReplays(steamFuncs, entries, selectedEntries));
            }

            GUIStyle replayLabelStyle = new GUIStyle(GUI.skin.label);
            replayLabelStyle.alignment = TextAnchor.MiddleLeft;
            replayLabelStyle.fontSize = (int)Math.Round(15f * scaleX);
            replayLabelStyle.normal.textColor = Color.white;

            GUIStyle checkMarkStyle = new GUIStyle(GUI.skin.label);
            checkMarkStyle.alignment = TextAnchor.MiddleCenter;
            checkMarkStyle.fontSize = (int)Math.Round(16f * scaleX);
            checkMarkStyle.fontStyle = FontStyle.Bold;
            checkMarkStyle.normal.textColor = Color.green;

            int entryCount = Mathf.Min(10, entries.Count);
            float startY = panelY + (float)Math.Round(103f * scaleY);
            float rowHeight = (float)Math.Round(33f * scaleY);

            float labelWidth = (float)Math.Round(93f * scaleX);
            float labelHeight = (float)Math.Round(28f * scaleY);

            float toggleOffsetX = (float)Math.Round((115f - 18f) * scaleX);
            float toggleOffsetY = (float)Math.Round(4f * scaleY);
            float toggleSize = (float)Math.Round(20f * scaleX);

            for (int i = 0; i < entryCount; i++)
            {
                float currentY = startY + (i * rowHeight);

                GUI.Label(new Rect(panelX + titleOffsetX, currentY, labelWidth, labelHeight), $"Replay #{i + 1}", replayLabelStyle);

                Rect toggleRect = new Rect(panelX + toggleOffsetX, currentY + toggleOffsetY, toggleSize, toggleSize);

                GUI.Box(toggleRect, "");

                if (selectedEntries[i])
                {
                    GUI.Label(toggleRect, "✓", checkMarkStyle);
                }

                if (GUI.Button(toggleRect, "", GUIStyle.none))
                {
                    selectedEntries[i] = !selectedEntries[i];
                }
            }
        }
    }
}