using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sharpcaster;
using Sharpcaster.Interfaces;
using Sharpcaster.Models.Media;

public class MusicService : BackgroundService
{
    private readonly Configuration _configuration;
    private readonly ILogger<MusicService> _logger;
    
    public MusicService(IOptions<Configuration> configuration, ILogger<MusicService> logger)
    {
        _configuration = configuration.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await PlaySong();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while playing song");
            }

            var timeToWait = CalculateTimeToWait();
            _logger.LogInformation("Waiting for {0:hh\\:mm\\:ss}", timeToWait);
            await Task.Delay(timeToWait, stoppingToken);
        }
    }

    private async Task PlaySong()
    {
        var random = new Random();
        var files = Directory.GetFiles(_configuration.AssetsPath, "*.mp3", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new FileNotFoundException("No mp3 file found");
        }
        var index = random.Next(0, files.Length);
        var file = files[index];
        
        IChromecastLocator locator = new MdnsChromecastLocator();
        var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        var chromecasts = await locator.FindReceiversAsync(source.Token);
        
        var chromecast = chromecasts.First(x => x.Name.Equals("Grupo primer piso"));
        var client = new ChromecastClient();
        await client.ConnectChromecast(chromecast);
        _ = await client.LaunchApplicationAsync("0B2D9915");

        var media = new Media
        {
            ContentUrl = $"http://{Environment.GetEnvironmentVariable("ASPNETCORE_URLS")!.Split(";").First()}/media/{Uri.EscapeDataString(Path.GetRelativePath(_configuration.AssetsPath, file))}"
        };
        _logger.LogInformation($"Playing: {media.ContentUrl}");
        _ = await client.MediaChannel.LoadAsync(media);
    }

    private string GetIp()
    {
        string ipAddress = "";
        if (Dns.GetHostAddresses(Dns.GetHostName()).Length > 0)
        {
            ipAddress = Dns.GetHostAddresses(Dns.GetHostName())[1].ToString();
        }
        return ipAddress;
    }
    
    public TimeSpan CalculateTimeToWait()
    {
        var current = TimeOnly.FromTimeSpan(DateTime.Now.TimeOfDay);

        var maxDelay = _configuration.MaximumTimeBetweenPlaybacks - _configuration.MinimumTimeBetweenPlaybacks;
        var randomWeight = new Random().NextDouble();
        var delay = maxDelay * randomWeight;
        
        if (current < _configuration.StartTime)
        {
            return _configuration.StartTime - current + delay;
        }
        
        if (current > _configuration.EndTime)
        {
            return _configuration.StartTime.ToTimeSpan().Add(TimeSpan.FromDays(1)) - current.ToTimeSpan() + delay;
        }
        
        var nextTime = current.ToTimeSpan() + delay;
        if (nextTime > _configuration.EndTime.ToTimeSpan())
        {
            return nextTime - _configuration.EndTime.ToTimeSpan() + _configuration.StartTime.ToTimeSpan().Add(TimeSpan.FromDays(1));
        }

        return delay;
    }
}