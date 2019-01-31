using Microsoft.Bot.Builder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PictureBot.Responses
{
    public class SearchResponses
    {
        public static async Task ReplyWithSearchRequest(ITurnContext context, string utterance)
        {
            await context.SendActivityAsync($"¿Qué quieres buscar?");
        }

        public static async Task ReplyWithSearchConfirmation(ITurnContext context, string utterance)
        {
            await context.SendActivityAsync($"De acuerdo, buscando por imágenes de {utterance}");
        }
        public static async Task ReplyWithNoResults(ITurnContext context, string utterance)
        {
            await context.SendActivityAsync("No se encontraron resultados para \"" + utterance + "\".");
        }
    }
}
