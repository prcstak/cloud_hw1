using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Yandex.Cloud.Functions;
using Ydb.Sdk;
using Ydb.Sdk.Services.Table;
using Ydb.Sdk.Value;
using Ydb.Sdk.Yc;

namespace Function;

public class Message
{
    public string body { get; set; }
}

public class Response
{
    public int statusCode { get; set; }
    public String body { get; set; }

    public Response(int statusCode, String body)
    {
        this.statusCode = statusCode;
        this.body = body;
    }
}

public class Handler
{
    private TableClient _tableClient;
    private string? _gatewayId;
    private AmazonS3Client _s3Client;
    private DriverConfig _driverConfig;

    public async Task<Response> FunctionHandler(string message, Context context)
    {
        await InitHandler();

        var tgevent = JsonConvert.DeserializeObject<Message>(message).body;
        var update = JsonConvert.DeserializeObject<Update>(tgevent);

        var tgClient = new TelegramBotClient(Environment.GetEnvironmentVariable("botToken"));

        await UpdateHandler(tgClient, update);

        return new Response(200, "OK");
    }

    private async Task InitHandler()
    {
        var accessKey = Environment.GetEnvironmentVariable("accessKey");
        var secretKey = Environment.GetEnvironmentVariable("secretKey");
        _gatewayId = Environment.GetEnvironmentVariable("gatewayId");

        _s3Client = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config { ServiceURL = "https://s3.yandexcloud.net" });
        
        var metadataProvider = new MetadataProvider();

        var connectionString = Environment.GetEnvironmentVariable("connectionString");
        var database = Environment.GetEnvironmentVariable("database");

        await metadataProvider.Initialize();

        _driverConfig = new DriverConfig(
            endpoint: connectionString!, 
            database: database!, 
            credentials: metadataProvider
        );
    }

    private async Task UpdateHandler(ITelegramBotClient botClient, Update update)
    {
        try
        {
            var message = update.Message;
            var chat = message?.Chat;

            if (message?.Text == null) return;

            if (message.Text == "/getface")
            {
                var faceId = await GetRandomFaceId();
                await SendPhotoFromObjectStorage(botClient, chat.Id, faceId, "vvot09-faces");
                return;
            }

            var entries = message.Text.Split(" ");

            if (entries[0] == "/find")
            {
                var name = entries[1];

                var images = await FindPhotoByFaceName(name);

                if (images.IsNullOrEmpty())
                {
                    await botClient.SendTextMessageAsync(chat.Id,
                        $"Фотографии с {name} не найдены");
                    return;
                }

                foreach (var image in images)
                {
                    await SendPhotoFromObjectStorage(botClient, chat.Id, image, "vvot09-photo");
                }

                return;
            }

            if (message.ReplyToMessage?.Caption != null)
            {
                var faceId = message.ReplyToMessage.Caption;
                await SetFaceName(faceId, message.Text);
                return;
            }

            await botClient.SendTextMessageAsync(chat.Id, "Ошибка");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task SendPhotoFromObjectStorage(ITelegramBotClient client, long chatId, string objectKey,
        string bucket)
    {
        var imageResponse = await _s3Client.GetObjectAsync(
            new GetObjectRequest
            {
                BucketName = bucket,
                Key = objectKey
            });
        await using (Stream responseStream = imageResponse.ResponseStream)
        {
            var url = GetObjectUrl(objectKey, bucket);
            await client.SendPhotoAsync(chatId,
                InputFile.FromStream(responseStream), caption: objectKey);
        }
    }

    private async Task<List<string>> FindPhotoByFaceName(string name)
    {
        using var driver = new Driver(
            config: _driverConfig
        );

        await driver.Initialize();

        using var tableClient = new TableClient(driver, new TableClientConfig());

        var response = await tableClient.SessionExec(async session =>
        {
            var query = @$"
                    DECLARE $name AS Utf8;

                    SELECT original_img FROM faces_table
                    WHERE name = $name
                ";

            return await session.ExecuteDataQuery(
                query: query,
                txControl: TxControl.BeginSerializableRW().Commit(),
                parameters: new Dictionary<string, YdbValue>
                {
                    { "$name", YdbValue.MakeUtf8(name) },
                }
            );
        });

        response.Status.EnsureSuccess();
        var queryResponse = (ExecuteDataQueryResponse)response;
        return queryResponse.Result.ResultSets[0].Rows.Select(r => (string)r["original_img"]).ToList();
    }

    private async Task<string> GetRandomFaceId()
    {
        using var driver = new Driver(
            config: _driverConfig
        );

        await driver.Initialize();

        using var tableClient = new TableClient(driver, new TableClientConfig());

        var response = await tableClient.SessionExec(async session =>
        {
            var query = @$"
                    SELECT id FROM faces_table
                    WHERE name IS null
                    ORDER BY RANDOM (id) 
                    LIMIT 1
                ";

            return await session.ExecuteDataQuery(
                query: query,
                txControl: TxControl.BeginSerializableRW().Commit()
            );
        });

        response.Status.EnsureSuccess();
        var queryResponse = (ExecuteDataQueryResponse)response;
        return queryResponse.Result.ResultSets[0].Rows.First()["id"].GetUtf8();
    }

    private async Task SetFaceName(string faceId, string name)
    {
        using var driver = new Driver(
            config: _driverConfig
        );

        await driver.Initialize();

        using var tableClient = new TableClient(driver, new TableClientConfig());

        var response = await tableClient.SessionExec(async session =>
        {
            var query = @$"
                    DECLARE $id AS Utf8;
                    DECLARE $name AS Utf8;

                    UPSERT INTO faces_table (id, name) VALUES
                        ($id, $name);
                ";

            return await session.ExecuteDataQuery(
                query: query,
                txControl: TxControl.BeginSerializableRW().Commit(),
                parameters: new Dictionary<string, YdbValue>
                {
                    { "$id", YdbValue.MakeUtf8(faceId) },
                    { "$name", YdbValue.MakeUtf8(name) },
                }
            );
        });

        response.Status.EnsureSuccess();
    }

    private string GetObjectUrl(string objectKey, string type)
    {
        return type switch
        {
            "vvot09-faces" => $"https://{_gatewayId}.apigw.yandexcloud.net/?face={objectKey}",
            "vvot09-photo" => $"https://{_gatewayId}.apigw.yandexcloud.net/{objectKey}"
        };
    }
}