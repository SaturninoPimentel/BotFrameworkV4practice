// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.PictureBot
{
    /// <summary>
    /// Stores counter state for the conversation.
    /// Stored in <see cref="Microsoft.Bot.Builder.ConversationState"/> and
    /// backed by <see cref="Microsoft.Bot.Builder.MemoryStorage"/>.
    /// </summary>
    public class PictureState
    {
        public string Greeted { get; set; } = "not greeted";
        public string Search { get; set; } = "";
        public string Searching { get; set; } = "no";
    }
}
