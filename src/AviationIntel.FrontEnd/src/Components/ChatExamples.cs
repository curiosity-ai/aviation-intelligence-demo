using System;
using System.Collections.Generic;
using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using TNT;
using static Tesserae.UI;
using static TNT.T;

namespace AviationIntel.FrontEnd.Components
{
    /// <summary>
    /// The example prompts of an assistant, as buttons that FILL the message box and nothing more: the user reads the question,
    /// edits it if they want, and presses send. (The chat view's own welcome prompts send at once, which is not what a demo
    /// presenter wants to do while talking.) Shown while the chat is empty; wired through <c>ChatViewConfiguration.CustomExamples</c>.
    /// </summary>
    internal static class ChatExamples
    {
        /// <summary>
        /// The chat context strip above the message box (the chat view mounts it in the welcome layout and over the composer). Built once;
        /// each example fills the box, none sends. It is the empty chat's content, so it goes away (collapses) once the conversation has
        /// started: <paramref name="empty"/> follows the view's own empty-state report.
        /// </summary>
        public static Func<CurrentChat, SettableObservable<ViewingContent>, OmniBox, IComponent> Context(string[] prompts, string prefill = null)
            => Context(prompts, SettableObservable.For(true), prefill);

        /// <summary>The same, collapsing once <paramref name="empty"/> turns false.</summary>
        public static Func<CurrentChat, SettableObservable<ViewingContent>, OmniBox, IComponent> Context(string[] prompts, SettableObservable<bool> empty, string prefill = null)
            => (chat, viewing, omniBox) =>
            {
                // A page can open the chat with a question already in the box (the briefing button): filled, never sent.
                if (!string.IsNullOrEmpty(prefill)) omniBox.ChatText = prefill;

                if (prompts is null || prompts.Length == 0) return Empty();

                var grid = HStack().Wrap().Gap(10.px()).WS().PB(14);

                foreach (var prompt in prompts)
                {
                    var text = prompt;

                    grid.Add(Button(text).SetIcon(UIcons.PencilPaintbrush).Class("av-example").Tooltip(text)
                        .OnClick(() => { omniBox.ChatText = text; omniBox.Focus(); }));
                }

                var strip = VStack().WS().Children(
                    TextBlock("Examples: click to fill the box, then send".t()).Tiny().Secondary().AlignCenter().PB(8),
                    grid);

                Action<bool> apply = isEmpty => { if (isEmpty) strip.Show(); else strip.Collapse(); };

                apply(empty.Value);

                var subscription = empty.Subscribe(apply);

                strip.WhenRemoved(() => subscription.Dispose());

                return strip;
            };
    }
}
