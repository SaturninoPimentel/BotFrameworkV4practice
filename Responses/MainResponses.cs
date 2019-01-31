using Microsoft.Bot.Builder;
using System.Threading.Tasks;

namespace PictureBot.Responses
{
    public class MainResponses
    {
        public static async Task ReplyWithGreeting(ITurnContext context)
        {
            await context.SendActivityAsync("Bienvenido a este pequeño bot...");
        }
        public static async Task ReplyWithHelp(ITurnContext context)
        {
            await context.SendActivityAsync($"Puedo buscar imágenes para ti.");
        }
        public static async Task ReplyWithResumeTopic(ITurnContext context)
        {
            await context.SendActivityAsync($"¿Qué puedo hacer para ti?");
        }
        public static async Task ReplyWithConfused(ITurnContext context)
        {
            await context.SendActivityAsync("No puedo entenderte. ¿Puedes interntar de nuevo?");
        }
        public static async Task ReplyWithLuisScore(ITurnContext context, string key, double score)
        {
            await context.SendActivityAsync($"Intent: {key} ({score}).");
        }
        public static async Task ReplyWithShareConfirmation(ITurnContext context)
        {
            await context.SendActivityAsync($"Publicando su foto(s) en twitter...");
        }
        public static async Task ReplyWithOrderConfirmation(ITurnContext context)
        {
            await context.SendActivityAsync($"Solicitud de impresiones estándar de sus imágenes...");
        }
    }
}
