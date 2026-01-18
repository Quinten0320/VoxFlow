using AiCallAssistent.Infrastructure.ElevenLabs.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;

namespace AiCallAssistent.API.Controllers
{
    [ApiController]
    [Route("api/twilio")]
    public class TwilioTestController : ControllerBase
    {
        private readonly ElevenLabsTextToSpeechService _tts;

        public TwilioTestController(ElevenLabsTextToSpeechService tts)
        {
            _tts = tts;
        }

        [HttpPost("test")]
        public async Task<IActionResult> Test()
        {
            Console.WriteLine("Twilio hit, ElevenLabs test");

            var audio = await _tts.SpeakAsync(
                "Test bericht, ik hou van patat");

            var fileName = $"{Guid.NewGuid()}.mp3";
            var path = Path.Combine("wwwroot/audio", fileName);

            Directory.CreateDirectory("wwwroot/audio");
            await System.IO.File.WriteAllBytesAsync(path, audio);

            var audioUrl = $"{Request.Scheme}://{Request.Host}/audio/{fileName}";

            var response = new Twilio.TwiML.VoiceResponse();
            response.Play(new Uri(audioUrl));

            return Content(response.ToString(), "text/xml");
        }
    }

}
