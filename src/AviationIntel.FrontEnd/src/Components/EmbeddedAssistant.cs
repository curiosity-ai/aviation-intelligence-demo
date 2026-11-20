using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Helpers;
using Mosaik.Schema;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;

namespace AviationIntel.FrontEnd.Components
{
    /// <summary>What the page the assistant sits on is showing: the records to attach and the words the model reads each turn.</summary>
    internal sealed class AssistantContext
    {
        /// <summary>Identity of the subject; a different key is a different subject.</summary>
        public string   Key         { get; set; }

        /// <summary>Short name of the subject, shown in the panel header ("Signal SIG-2025-003").</summary>
        public string   Label       { get; set; }

        /// <summary>Hidden text sent with every message (the transcript shows what the user typed). Says what the page shows now.</summary>
        public string   Description { get; set; }

        /// <summary>Records attached to the conversation, each once per conversation: the assistant consults them by id.</summary>
        public UID128[] Records     { get; set; }
    }

    internal sealed class EmbeddedAssistantOptions
    {
        /// <summary>Names the page in the browser storage key that remembers the collapsed state, and in the text the model reads.</summary>
        public string                  PageKey        { get; set; }
        public string                  PageTitle      { get; set; }
        public string                  WelcomeTitle   { get; set; }
        public string[]                WelcomePrompts { get; set; }
        public string                  Placeholder    { get; set; }

        /// <summary>The subject, when the page knows it up front. A page whose subject changes calls <see cref="EmbeddedAssistant.SetContext"/>.</summary>
        public Task<AssistantContext>  Context        { get; set; }

        public bool                    ExpandedByDefault { get; set; } = true;
        public int                     Height         { get; set; } = 460;

        /// <summary>The panel fills the height of the window and stays in view while the page scrolls (a column beside the content), instead of a card of <see cref="Height"/> px.</summary>
        public bool                    Docked         { get; set; }

        /// <summary>Opens a record named in an answer. Return false to let the default (the record's preview sheet) do it.</summary>
        public System.Func<UID128, bool> OnOpenRecord { get; set; }
    }

    /// <summary>
    /// The assistant of the signed-in desk, docked inside a page: the same assistants and the same reply parsing as the
    /// global chat page, with the page's subject attached. A conversation started here is an ordinary chat, so it is in the
    /// history of the chat page and "Open in the full chat" continues it there.
    /// </summary>
    internal sealed class EmbeddedAssistant : IComponent
    {
        private readonly EmbeddedAssistantOptions _options;
        private readonly UID128                   _assistant;
        private readonly ChatView                 _chat;
        private readonly IComponent               _root;
        private readonly Stack                    _body;
        private readonly Button                   _toggle;
        private readonly TextBlock                _contextLabel;
        private readonly string                   _storageKey;
        private readonly HashSet<string>          _attached = new HashSet<string>();
        private readonly SettableObservable<bool> _empty    = SettableObservable.For(true);

        private Task<AssistantContext> _context;
        private ChatMetadata           _current;
        private bool                   _expanded;

        public EmbeddedAssistant(EmbeddedAssistantOptions options)
        {
            _options    = options;
            _assistant  = AskAssistant.AssistantUID;
            _context    = options.Context;
            _storageKey = "aviation-assistant-expanded:" + options.PageKey;

            _chat = new ChatView(BuildConfiguration(), new Parameters());
            _chat.SelectedChat.Observe(chat => _current = chat);

            _expanded = ReadExpanded(options.ExpandedByDefault);

            _toggle       = Button().SetIcon(_expanded ? UIcons.AngleUp : UIcons.AngleDown).Tooltip((_expanded ? "Collapse" : "Expand").t()).OnClick(() => SetExpanded(!_expanded));
            _contextLabel = TextBlock("").Tiny().Secondary().NoWrap().Ellipsis();

            var newChat = Button().SetIcon(UIcons.Edit).Tooltip("New conversation".t()).OnClick(() => { SetExpanded(true); _chat.StartNewChat(); });
            var full    = Button().SetIcon(UIcons.ArrowUpRightFromSquare).Tooltip("Continue in the full chat".t()).OnClick(OpenFullChat);

            var title = HStack().AlignItemsCenter().Grow().NoWrap().Children(
                    IconTile(UIcons.Sparkles, Theme.Colors.Blue600).Size(24.px()),
                    VStack().PL(8).Grow().Children(
                        TextBlock("Aviation Market Intelligence".t()).SemiBold(),
                        _contextLabel));

            var header = HStack().WS().AlignItemsCenter().NoWrap().Children(title, newChat, full, _toggle);

            _body = options.Docked
                ? VStack().WS().Grow().PT(8).Style(st => st.minHeight = "0").Children(_chat.S())
                : VStack().WS().H(options.Height).PT(8).Children(_chat.S());

            if (!_expanded) _body.Collapse();

            var card = Card(VStack().WS().Children(header, _body)).WS().Class("av-assistant");

            if (options.Docked)
            {
                // Stays in view while the page scrolls past it; the transcript scrolls inside.
                card = card.Class("av-docked");
            }

            _root = card;

            if (_context is object) _context.ContinueWith(t => ApplyLabel(t.Result));
        }

        public bool IsExpanded => _expanded;

        /// <summary>Opens the panel if it was collapsed and sends the question into the conversation in view.</summary>
        public void Ask(string question)
        {
            SetExpanded(true);
            _chat.SendMessageOnCurrentChat(question).FireAndForget();
        }

        /// <summary>The page now shows something else. The conversation goes on; the new records join it with the next message.</summary>
        public void SetContext(AssistantContext context)
        {
            _context = Task.FromResult(context);
            ApplyLabel(context);
        }

        public void ScrollIntoView() => _root.Render().scrollIntoView();

        public Transpose.Core.dom.HTMLElement Render() => _root.Render();

        // ----- state ------------------------------------------------------------------------------

        private bool ReadExpanded(bool byDefault)
        {
            var stored = LocalStorage.Get(_storageKey);

            return stored is null ? byDefault : stored == "true";
        }

        private void SetExpanded(bool expanded)
        {
            if (_expanded == expanded) return;

            _expanded = expanded;

            if (expanded) _body.Show(); else _body.Collapse();

            _toggle.SetIcon(expanded ? UIcons.AngleUp : UIcons.AngleDown).Tooltip((expanded ? "Collapse" : "Expand").t());

            LocalStorage.Set(_storageKey, expanded ? "true" : "false");
        }

        private void ApplyLabel(AssistantContext context)
            => _contextLabel.Text = context is null || string.IsNullOrEmpty(context.Label) ? "" : string.Format("About: {0}".t(), context.Label);

        private void OpenFullChat()
        {
            if (_current is null)
            {
                Router.Navigate(DefaultRoutes.ChatAI.Base);
                return;
            }

            Router.Navigate(DefaultRoutes.ChatAI.OpenChat(_current.UID, null), reload: true);
        }

        private bool OpenRecord(UID128 uid)
        {
            if (UID128.IsNull(uid)) return false;

            if (_options.OnOpenRecord is object && _options.OnOpenRecord(uid)) return true;

            NodePreview.For(uid, null, true);
            return true;
        }

        // ----- the conversation -------------------------------------------------------------------

        private async Task<AssistantContext> ContextAsync() => _context is null ? null : await _context;

        private static UID128[] RecordsOf(AssistantContext context)
            => context?.Records is null ? new UID128[0] : context.Records.Where(u => !UID128.IsNull(u)).Distinct().ToArray();

        private string Describe(AssistantContext context)
            => context is null || string.IsNullOrEmpty(context.Description)
                ? string.Format("The user is on the {0} page of the application.", _options.PageTitle)
                : string.Format("The user is on the {0} page of the application. What the page shows now: {1} Records named here are attached to this conversation; 'this' and 'it' refer to them.", _options.PageTitle, context.Description);

        private async Task AttachNewRecordsAsync(UID128 chatUID, AssistantContext context)
        {
            var fresh = RecordsOf(context).Where(r => !_attached.Contains(chatUID + "|" + r)).ToArray();
            if (fresh.Length == 0) return;

            foreach (var r in fresh) _attached.Add(chatUID + "|" + r);

            await Mosaik.API.ChatAI.AddContextToChat(chatUID, fresh);
        }

        private async Task<ChatMetadata> NewChatAsync(ChatViewConfiguration.NewChatRequest request)
        {
            var context  = await ContextAsync();
            var records  = RecordsOf(context);
            var provider = request.ProviderUID ?? App.InterfaceSettings.ChatAIProvider?.TaskUID;

            var chat = records.Length == 0
                ? await Mosaik.API.ChatAI.NewChat(provider, _assistant, request.Category)
                : await Mosaik.API.ChatAI.NewChatWithContext(provider, _assistant, records);

            if (chat is object) foreach (var r in records) _attached.Add(chat.UID + "|" + r);

            return chat;
        }

        private async Task<UID128> PostAsync(ChatViewConfiguration.PostMessageRequest request)
        {
            var context = await ContextAsync();

            await AttachNewRecordsAsync(request.ActiveChat.UID, context);

            // The same pinned capability set as the chat page: the composer has no picker.
            return await Mosaik.API.ChatAI.PostMessage(
                chatUID: request.ActiveChat.UID, message: request.Message, useTools: AviationChatView.PinnedTools, useAgents: AviationChatView.PinnedAgents,
                useSkills: request.ActiveSkills, uiContext: Describe(context), cancellationToken: request.CancellationToken);
        }

        private async Task<UID128> ReplaceAsync(ChatViewConfiguration.ReplaceMessageRequest request)
        {
            var context = await ContextAsync();

            return await Mosaik.API.ChatAI.ReplaceMessage(
                chatUID: request.ActiveChat.UID, messageUID: request.MessageUID, message: request.Message, useTools: AviationChatView.PinnedTools, useAgents: AviationChatView.PinnedAgents,
                useSkills: request.ActiveSkills, uiContext: Describe(context), cancellationToken: request.CancellationToken);
        }

        private ChatViewConfiguration BuildConfiguration() => new ChatViewConfiguration
        {
            CompactMode              = true,
            ChatRoute                = _ => null,
            ChatHistoryInitiallyOpen = false,

            DisableAssistantSelector = true,
            DisableProviderSelector  = true,
            DisableTools             = true,
            DisableDictate           = true,
            Attachments              = ChatAttachmentsMode.Disabled,

            WelcomeTitle             = _options.WelcomeTitle,
            DisableWelcomePrompts    = true,
            CustomChatContext        = ChatExamples.Context(_options.WelcomePrompts, _empty),
            OnEmptyStateChanged      = isEmpty => _empty.Value = isEmpty,
            ChatBoxPlaceholderText   = _options.Placeholder,

            ParseAs                  = AviationChatView.ParseAs(),

            // Records open where the user is working, not in a split view inside a panel a third of the page wide.
            OnOpenPreview            = content => content is object && OpenRecord(content.UID),
            OnCitationClick          = OpenRecord,

            // The conversations are listed on the chat page; here the history would only take width from the panel.
            CustomChatHistory        = (configuration, selectedChat, existingChats, chatStates) => new ChatHistory(
                existingChats, selectedChat, configuration,
                onChatSelected:   chat => selectedChat.Value = chat,
                onNewChatClicked: ()   => selectedChat.Value = null,
                currentChatStates: chatStates,
                mountToView:       false),

            NewChat                  = NewChatAsync,
            PostMessage              = PostAsync,
            ReplaceMessage           = ReplaceAsync,
        };
    }
}
