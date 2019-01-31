// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Extensions.Logging;
using System.Linq;
using PictureBot.Models;
using PictureBot.Responses;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.PictureBot
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each user interaction, an instance of this class is created and the OnTurnAsync method is called.
    /// This is a Transient lifetime service.  Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single turn, should be carefully managed.
    /// For example, the <see cref="MemoryStorage"/> object and associated
    /// <see cref="IStatePropertyAccessor{T}"/> object are created with a singleton lifetime.
    /// </summary>
    /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.1"/>
    public class PictureBot : IBot
    {
        private readonly PictureBotAccessors _accessors;
        private readonly ILogger _logger;
        private DialogSet _dialogs;
        private LuisRecognizer _recognizer { get; } = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="PictureBot"/> class.
        /// </summary>
        /// <param name="accessors">A class containing <see cref="IStatePropertyAccessor{T}"/> used to manage state.</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public PictureBot(PictureBotAccessors accessors, ILoggerFactory loggerFactory,
            LuisRecognizer recognizer)
        {
            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            // Lab 2.2.3 Add instance of LUIS Recognizer
            _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));

            _logger = loggerFactory.CreateLogger<PictureBot>();
            _logger.LogTrace("PictureBot turn start.");
            _accessors = accessors ?? throw new System.ArgumentNullException(nameof(accessors));

            // The DialogSet needs a DialogState accessor, it will call it when it has a turn context.
            _dialogs = new DialogSet(_accessors.DialogStateAccessor);

            // This array defines how the Waterfall will execute.
            // We can define the different dialogs and their steps here
            // allowing for overlap as needed. In this case, it's fairly simple
            // but in more complex scenarios, you may want to separate out the different
            // dialogs into different files.
            WaterfallStep[] main_waterfallsteps = new WaterfallStep[]
            {
                GreetingAsync,
                MainMenuAsync,
            };
            WaterfallStep[] search_waterfallsteps = new WaterfallStep[]
            {
                SearchRequestAsync,
                SearchAsync
            };

            // Add named dialogs to the DialogSet. These names are saved in the dialog state.
            _dialogs.Add(new WaterfallDialog("mainDialog", main_waterfallsteps));
            _dialogs.Add(new WaterfallDialog("searchDialog", search_waterfallsteps));
            // The following line allows us to use a prompt within the dialogs
            _dialogs.Add(new TextPrompt("searchPrompt"));
        }

        private async Task<DialogTurnResult> SearchAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            PictureState state =
                await _accessors.PictureState.GetAsync(stepContext.Context);
            if (state.Search == string.Empty)
            {
                state.Search = (string)stepContext.Result;
                await _accessors.ConversationState.SaveChangesAsync(stepContext.Context);
            }

            var searchText = state.Search;

            await SearchResponses.ReplyWithSearchConfirmation(stepContext.Context, searchText);

            await StartAsync(stepContext.Context, searchText);

            state.Search = string.Empty;
            state.Searching = "no";
            await _accessors.ConversationState.SaveChangesAsync(stepContext.Context);

            return await stepContext.EndDialogAsync();
        }

        private async Task StartAsync(ITurnContext context, string searchText)
        {
            ISearchIndexClient indexClient =
                CreateSearchIndexClient();
            DocumentSearchResult results =
                await indexClient.Documents.SearchAsync(searchText);
            await SendResultsAsync(context, searchText, results);
        }

        private async Task SendResultsAsync(ITurnContext context, string searchText, DocumentSearchResult results)
        {
            IMessageActivity activity =
                context.Activity.CreateReply();

            if (!results.Results.Any())
            {
                await SearchResponses.ReplyWithNoResults(context, searchText);
            }
            else
            {
                SearchHitStyler searchHitStyler = new SearchHitStyler();
                searchHitStyler.Apply(ref activity,
                    "Here are the results that i found",
                    results.Results.Select(r =>
                        ImageMapper.ToSearchHit(r)).ToList().AsReadOnly());
                await context.SendActivityAsync(activity);
            }
        }

        private ISearchIndexClient CreateSearchIndexClient()
        {
            var configurationBuilder =
                new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json");

            IConfigurationRoot configuration = configurationBuilder.Build();


            string searchServiceName = configuration["AppSettings:searchServiceName"];
            string queryApiKey = configuration["AppSettings:queryApiKey"];
            string indexName = "images";

            SearchIndexClient indexClient =
                new SearchIndexClient(searchServiceName,
                    indexName,
                    new SearchCredentials(queryApiKey));
            return indexClient;

        }

        private async Task<DialogTurnResult> SearchRequestAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            PictureState state =
                await _accessors.PictureState.GetAsync(stepContext.Context);
            if (state.Searching == "no")
            {
                state.Searching = "yes";
                await _accessors.ConversationState.SaveChangesAsync(stepContext.Context);

                return await stepContext.PromptAsync("searchPrompt",
                    new PromptOptions()
                    {
                        Prompt = MessageFactory.Text("¿Qué te gustaría buscar?")
                    },
                    cancellationToken);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> MainMenuAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Check if we are currently processing a user's search
            PictureState state = await _accessors.PictureState.GetAsync(stepContext.Context);

            // If Regex picks up on anything, store it
            IRecognizedIntents recognizedIntents = stepContext.Context.TurnState.Get<IRecognizedIntents>();
            // Based on the recognized intent, direct the conversation
            switch (recognizedIntents.TopIntent?.Name)
            {
                case "search":
                    // switch to the search dialog
                    return await stepContext.BeginDialogAsync("searchDialog", null, cancellationToken);
                case "share":
                    // respond that you're sharing the photo
                    await MainResponses.ReplyWithShareConfirmation(stepContext.Context);
                    return await stepContext.EndDialogAsync();
                case "order":
                    // respond that you're ordering
                    await MainResponses.ReplyWithOrderConfirmation(stepContext.Context);
                    return await stepContext.EndDialogAsync();
                case "help":
                    // show help
                    await MainResponses.ReplyWithHelp(stepContext.Context);
                    return await stepContext.EndDialogAsync();
                default:
                    {
                        return await BetterCallToLuis(stepContext, cancellationToken, state);
                    }
            }
        }

        private async Task<DialogTurnResult> BetterCallToLuis(
            WaterfallStepContext stepContext,
            CancellationToken cancellationToken,
            PictureState state)
        {
            // Call LUIS recognizer
            var result = await _recognizer.RecognizeAsync(stepContext.Context, cancellationToken);
            // Get the top intent from the results
            var topIntent = result?.GetTopScoringIntent();
            // Based on the intent, switch the conversation, similar concept as with Regex above
            switch ((topIntent != null) ? topIntent.Value.intent : null)
            {
                case null:
                    // Add app logic when there is no result.
                    await MainResponses.ReplyWithConfused(stepContext.Context);
                    break;
                case "None":
                    await MainResponses.ReplyWithConfused(stepContext.Context);
                    // with each statement, we're adding the LuisScore, purely to test, so we know whether LUIS was called or not
                    await MainResponses.ReplyWithLuisScore(stepContext.Context, topIntent.Value.intent, topIntent.Value.score);
                    break;
                case "Greeting":
                    await MainResponses.ReplyWithGreeting(stepContext.Context);
                    await MainResponses.ReplyWithHelp(stepContext.Context);
                    await MainResponses.ReplyWithLuisScore(stepContext.Context, topIntent.Value.intent, topIntent.Value.score);
                    break;
                case "OrderPic":
                    await MainResponses.ReplyWithOrderConfirmation(stepContext.Context);
                    await MainResponses.ReplyWithLuisScore(stepContext.Context, topIntent.Value.intent, topIntent.Value.score);
                    break;
                case "SharePic":
                    await MainResponses.ReplyWithShareConfirmation(stepContext.Context);
                    await MainResponses.ReplyWithLuisScore(stepContext.Context, topIntent.Value.intent, topIntent.Value.score);
                    break;
                case "SearchPics":
                    // Check if LUIS has identified the search term that we should look for.  
                    // Note: you should have stored the search term as "facet", but if you did not,
                    // you will need to update.
                    var entity = result?.Entities;
                    var obj = JObject.Parse(JsonConvert.SerializeObject(entity)).SelectToken("facet");

                    // If entities are picked up on by LUIS, store them in state.Search
                    // Also, update state.Searching to "yes", so you don't ask the user
                    // what they want to search for, they've already told you
                    if (obj != null)
                    {
                        // format "facet", update state, and save save
                        state.Search = obj.ToString().Replace("\"", "").Trim(']', '[').Trim();
                        state.Searching = "yes";
                        await _accessors.ConversationState.SaveChangesAsync(stepContext.Context);
                    }

                    // Begin the search dialog
                    await MainResponses.ReplyWithLuisScore(stepContext.Context, topIntent.Value.intent, topIntent.Value.score);
                    return await stepContext.BeginDialogAsync("searchDialog", null, cancellationToken);
                default:
                    await MainResponses.ReplyWithConfused(stepContext.Context);
                    break;
            }
            return await stepContext.EndDialogAsync();
        }

        private async Task<DialogTurnResult> GreetingAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the state for the current step in the conversation
            PictureState state = await _accessors.PictureState.GetAsync(stepContext.Context, () => new PictureState());


            // If we haven't greeted the user
            if (state.Greeted == "not greeted")
            {
                // Greet the user
                await MainResponses.ReplyWithGreeting(stepContext.Context);
                // Update the GreetedState to greeted
                state.Greeted = "greeted";
                // Save the new greeted state into the conversation state
                // This is to ensure in future turns we do not greet the user again
                await _accessors.ConversationState.SaveChangesAsync(stepContext.Context);
                // Ask the user what they want to do next
                await MainResponses.ReplyWithHelp(stepContext.Context);
                // Since we aren't explicitly prompting the user in this step, we'll end the dialog
                // When the user replies, since state is maintained, the else clause will move them
                // to the next waterfall step
                return await stepContext.EndDialogAsync();
            }
            else // We've already greeted the user
            {
                // Move to the next waterfall step, which is MainMenuAsync
                return await stepContext.NextAsync();
            }
        }

        /// <summary>
        ///Every conversation turn for our picture bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response. Later, when we add Dialogs, we'll have to navigate through this method.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (turnContext.Activity.Type is "message")
            {
                // Establish dialog context from the conversation state.
                var dc = await _dialogs.CreateContextAsync(turnContext);
                // Continue any current dialog.
                var results = await dc.ContinueDialogAsync(cancellationToken);

                // Every turn sends a response, so if no response was sent,
                // then there no dialog is currently active.
                if (!turnContext.Responded)
                {
                    // Start the main dialog
                    await dc.BeginDialogAsync("mainDialog", null, cancellationToken);
                }
            }
        }
    }
}
