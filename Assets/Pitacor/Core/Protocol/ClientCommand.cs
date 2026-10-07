using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Rules;

namespace Pitacor.Core.Protocol
{
    /// <summary>Stable values for ClientCommand.Type.</summary>
    public static class CommandTypes
    {
        public const string UpdateSettings = "UpdateSettings";
        public const string ReorderPlayers = "ReorderPlayers";
        public const string StartMatch = "StartMatch";
        public const string SubmitClue1 = "SubmitClue1";
        public const string SelectCell = "SelectCell";
        public const string ConfirmGuess = "ConfirmGuess";
        public const string SubmitClue2 = "SubmitClue2";
        public const string PassClue2 = "PassClue2";
        public const string SetReady = "SetReady";
        public const string HostSkipPhase = "HostSkipPhase";
        public const string HostEndMatch = "HostEndMatch";
        public const string Rematch = "Rematch";
        public const string SendChat = "SendChat";
        public const string SendReaction = "SendReaction";
        public const string Kick = "Kick";
        public const string VoteKick = "VoteKick";
        public const string Report = "Report";
        public const string UpdateProfile = "UpdateProfile";
    }

    /// <summary>
    /// The single client-to-server command DTO. Only the fields relevant to <see cref="Type"/> are read.
    /// The static factories build well-formed commands (used by the client and by tests).
    /// </summary>
    public class ClientCommand
    {
        public string Type { get; set; }
        /// <summary>UpdateSettings.</summary>
        public GameSettings Settings { get; set; }
        /// <summary>ReorderPlayers: every player id in the new rotation order.</summary>
        public List<string> Order { get; set; }
        /// <summary>SubmitClue1: 0..3.</summary>
        public int? OptionIndex { get; set; }
        /// <summary>SubmitClue1, SubmitClue2, SendChat, Report.</summary>
        public string Text { get; set; }
        /// <summary>SelectCell, ConfirmGuess.</summary>
        public BoardCoord? Coord { get; set; }
        /// <summary>SendReaction: one of <see cref="ReactionCodes"/>.</summary>
        public string ReactionCode { get; set; }
        /// <summary>Kick, VoteKick, Report.</summary>
        public string TargetPlayerId { get; set; }
        /// <summary>UpdateProfile (null keeps the current nickname).</summary>
        public string Nickname { get; set; }
        /// <summary>UpdateProfile (null keeps the current avatar).</summary>
        public AvatarSpec Avatar { get; set; }

        public static ClientCommand Of(string type) => new ClientCommand { Type = type };

        public static ClientCommand UpdateSettingsTo(GameSettings settings) =>
            new ClientCommand { Type = CommandTypes.UpdateSettings, Settings = settings };

        public static ClientCommand Reorder(List<string> order) => new ClientCommand { Type = CommandTypes.ReorderPlayers, Order = order };

        public static ClientCommand SubmitClue1(int optionIndex, string text) =>
            new ClientCommand { Type = CommandTypes.SubmitClue1, OptionIndex = optionIndex, Text = text };

        public static ClientCommand SelectCell(BoardCoord coord) => new ClientCommand { Type = CommandTypes.SelectCell, Coord = coord };

        public static ClientCommand ConfirmGuess(BoardCoord coord) => new ClientCommand { Type = CommandTypes.ConfirmGuess, Coord = coord };

        public static ClientCommand SubmitClue2(string text) => new ClientCommand { Type = CommandTypes.SubmitClue2, Text = text };

        public static ClientCommand Chat(string text) => new ClientCommand { Type = CommandTypes.SendChat, Text = text };

        public static ClientCommand Reaction(string code) => new ClientCommand { Type = CommandTypes.SendReaction, ReactionCode = code };

        public static ClientCommand Kick(string targetPlayerId) => new ClientCommand { Type = CommandTypes.Kick, TargetPlayerId = targetPlayerId };

        public static ClientCommand VoteKick(string targetPlayerId) =>
            new ClientCommand { Type = CommandTypes.VoteKick, TargetPlayerId = targetPlayerId };

        public static ClientCommand Report(string targetPlayerId, string text) =>
            new ClientCommand { Type = CommandTypes.Report, TargetPlayerId = targetPlayerId, Text = text };

        public static ClientCommand UpdateProfile(string nickname, AvatarSpec avatar) =>
            new ClientCommand { Type = CommandTypes.UpdateProfile, Nickname = nickname, Avatar = avatar };
    }

    /// <summary>The fixed set of quick reactions.</summary>
    public static class ReactionCodes
    {
        public const string Laugh = "laugh";
        public const string Wow = "wow";
        public const string Fire = "fire";
        public const string Clap = "clap";
        public const string Think = "think";
        public const string Heart = "heart";
        public const string Facepalm = "facepalm";
        public const string Party = "party";

        public static readonly string[] All = { Laugh, Wow, Fire, Clap, Think, Heart, Facepalm, Party };

        public static bool IsValid(string code)
        {
            if (code == null) return false;
            for (int i = 0; i < All.Length; i++)
                if (All[i] == code) return true;
            return false;
        }
    }
}
