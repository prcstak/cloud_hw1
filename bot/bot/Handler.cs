using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Yandex.Cloud;
using Yandex.Cloud.Credentials;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Yandex.Cloud.Functions;
using Ydb.Sdk;
using Ydb.Sdk.Services.Table;
using Ydb.Sdk.Value;
using Ydb.Sdk.Yc;

namespace Function;

public class Message {
    public string body { get; set; }
}

public class Response {
    public int statusCode { get; set; }
    public String body { get; set; }

    public Response(int statusCode, String body) {
        this.statusCode = statusCode;
        this.body = body;
    }
}

public class Handler
{
    public async Task<Response> FunctionHandler(string message, Context context)
    {
        var tgevent = JsonConvert.DeserializeObject<Message>(message).body;
        var update = JsonConvert.DeserializeObject<Update>(tgevent);
        
        var tgClient = new TelegramBotClient("6911449123:AAFoIYdoptbkzU1vXFApetQKkhMHCLp0HrA");

        await UpdateHandler(tgClient, update);

        return new Response(200, "OK");
    }

    private async Task UpdateHandler(ITelegramBotClient botClient, Update update)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message:
                {
                    var message = update.Message;
                    var chat = message?.Chat;

                    switch (message.Type)
                    {
                        case MessageType.Text:
                        {
                            if (message.Text == "/getface")
                            {
                                var faceId = await GetRandomFaceId();
                                await SendPhoto(botClient, chat.Id, faceId);
                            }
                            else
                            {
                                if (message.ReplyToMessage?.Caption != null)
                                {
                                    var faceId = message.ReplyToMessage.Caption;
                                    await SetFaceName(faceId, message.Text);
                                }
                            }
                            break;
                        }
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private async Task SendPhoto(ITelegramBotClient client, long chatId, string faceKey)
    {
        var accessKey = Environment.GetEnvironmentVariable("accessKey");
        var secretKey = Environment.GetEnvironmentVariable("secretKey");
        
        AmazonS3Client s3Client = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config { ServiceURL = "https://s3.yandexcloud.net" });

        var imageResponse = await s3Client.GetObjectAsync(
            new GetObjectRequest
            {
                BucketName = "vvot09-faces",
                Key = faceKey
            });
        await using (Stream responseStream = imageResponse.ResponseStream)
        {
            var gatewayId = Environment.GetEnvironmentVariable("gatewayId");
            var url = $"https://{gatewayId}.apigw.yandexcloud.net/?face={faceKey}";
            Console.WriteLine(url);
            await client.SendPhotoAsync(chatId,
                InputFile.FromStream(responseStream), caption: faceKey);
        }
        
    }

    private async Task<string> GetRandomFaceId()
    {
        var connectionString = Environment.GetEnvironmentVariable("connectionString");
        var database = Environment.GetEnvironmentVariable("database");
        
        var metadataProvider = new MetadataProvider();

        // Await initial IAM token.
        
        await metadataProvider.Initialize();
        
        var config = new DriverConfig(
            endpoint: connectionString!, // Database endpoint, "grpcs://host:port"
            database: database!, // Full database path
            credentials: metadataProvider
        );

        using var driver = new Driver(
            config: config
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
        var connectionString = Environment.GetEnvironmentVariable("connectionString");
        var database = Environment.GetEnvironmentVariable("database");
        
        var metadataProvider = new MetadataProvider();

        // Await initial IAM token.
        
        await metadataProvider.Initialize();
        
        var config = new DriverConfig(
            endpoint: connectionString!, // Database endpoint, "grpcs://host:port"
            database: database!, // Full database path
            credentials: metadataProvider
        );

        using var driver = new Driver(
            config: config
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
}