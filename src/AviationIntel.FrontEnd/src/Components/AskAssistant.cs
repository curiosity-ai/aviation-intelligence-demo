using System;
using System.Threading.Tasks;
using Mosaik;
using Tesserae;
using UID;
using static Tesserae.UI;
using static TNT.T;

namespace AviationIntel.FrontEnd.Components
{
    /// <summary>
    /// Opens a chat on the Aviation Market Intelligence assistant and asks it something - the one
    /// way every button in this front end reaches the AI.
    /// </summary>
    /// <remarks>
    /// The assistant is named by UID rather than taken from the user's selected template: it is the
    /// one carrying the news desk sandbox, the report skill and the analyst agent, and the turn names
    /// all three explicitly so a report request never lands on a turn without its tools.
    /// </remarks>
    internal static class AskAssistant
    {
        /// <summary><c>code/ai-assistants/aviation-market-intelligence-AVAssistant11111111111.cs</c>.</summary>
        internal static readonly UID128 AssistantUID = UID128.Parse("AVAssistant11111111111");

        /// <summary><c>code/chat-ai-tools/AVsandboX1111111111111.cs</c> - the Aviation News Desk.</summary>
        internal static readonly UID128 NewsDeskToolUID = UID128.Parse("AVsandboX1111111111111");

        /// <summary><c>code/agents/aviation-market-intelligence-analyst-AVAgent111111111111111.cs</c>.</summary>
        internal static readonly UID128 AnalystAgentUID = UID128.Parse("AVAgent111111111111111");

        /// <summary><c>code/skills/aviation-market-reports-Avreport11111111111111.md</c>.</summary>
        internal static readonly UID128 ReportSkillUID = UID128.Parse("Avreport11111111111111");

        /// <summary>
        /// Creates the chat, posts the question and navigates to it. The post is not awaited before
        /// navigating, so the chat opens onto an answer that is already being written.
        /// </summary>
        public static async Task AskAsync(string question)
        {
            var chat = await Mosaik.API.ChatAI.NewChat(App.InterfaceSettings.ChatAIProvider?.TaskUID ?? UID128.Empty, AssistantUID);

            if (chat is null)
            {
                Toast().Warning("The assistant could not be opened".t(), "Check that the Aviation Market Intelligence assistant is present in this workspace.".t());
                return;
            }

            Mosaik.API.ChatAI.PostMessage(chat.UID, question,
                                          useTools:  new[] { NewsDeskToolUID },
                                          useAgents: new[] { AnalystAgentUID },
                                          useSkills: new[] { ReportSkillUID }).FireAndForget();

            Router.Navigate(DefaultRoutes.ChatAI.OpenChat(chat.UID, UID128.Empty), reload: true);
        }

        /// <summary>The daily brief for today (UTC), both formats.</summary>
        public static Task DailyBriefAsync() => AskAsync(string.Format(
            "Write the daily aviation market brief for {0} (the last 24 hours) and publish it as the HTML report and the PowerPoint deck.",
            DateTime.UtcNow.ToString("dddd d MMMM yyyy")));

        /// <summary>The weekly report for the last seven days, both formats.</summary>
        public static Task WeeklyReportAsync() => AskAsync(string.Format(
            "Write the weekly aviation market report for the seven days to {0} and publish it as the HTML report and the PowerPoint deck.",
            DateTime.UtcNow.ToString("d MMMM yyyy")));
    }
}
