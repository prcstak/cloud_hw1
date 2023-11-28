using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Aspose.Imaging;
using Yandex.Cloud;
using Yandex.Cloud.Credentials;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ydb.Sdk;
using Ydb.Sdk.Auth;
using Ydb.Sdk.Services.Table;
using Ydb.Sdk.Value;
using Ydb.Sdk.Yc;

namespace Function;

public class CutTask
{
    public Face face { get; set; }
    public string? objectId { get; set; }
}

public class BoundingBox
{
    public List<Vertex> Vertices { get; set; }
}

public class Face
{
    public BoundingBox BoundingBox { get; set; }
}

public class Root
{
    public Face face { get; set; }
    public string objectId { get; set; }
}

public class Vertex
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class Handler
{
    private readonly Sdk _sdk =
        new(new OAuthCredentialsProvider("y0_AgAAAABwYfFqAATuwQAAAADubwuME5d0zbOnQ3CxT3pw6Hc7feKYU3I"));

    public async Task FunctionHandler(string message)
    {
        var accessKey = Environment.GetEnvironmentVariable("accessKey");
        var secretKey = Environment.GetEnvironmentVariable("secretKey");
        var connectionString = Environment.GetEnvironmentVariable("connectionString");
        var database = Environment.GetEnvironmentVariable("database");
        Console.WriteLine(database);

        var jsonData = JsonConvert.DeserializeObject<JToken>(message)!;
        var jsonTask = (jsonData["messages"]![0]!["details"]?["message"]?["body"]!).Value<string>()!;

        var task = JsonConvert.DeserializeObject<Root>(jsonTask)!;

        var key = Guid.NewGuid().ToString();


        AmazonS3Client s3Client = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config { ServiceURL = "https://s3.yandexcloud.net" });

        var imageResponse = await s3Client.GetObjectAsync(
            new GetObjectRequest
            {
                BucketName = "vvot09-photo",
                Key = task.objectId
            });

        var transfer = new TransferUtility(s3Client);

        await using (Stream responseStream = imageResponse.ResponseStream)
        {
            byte[] b;
            using (var memoryStream = new MemoryStream())
            {
                await responseStream.CopyToAsync(memoryStream);
                b = memoryStream.ToArray();
            }

            using (MemoryStream stream = new MemoryStream(b))

            using (RasterImage rasterImage = (RasterImage)Image.Load(stream))
            {
                var box = task.face.BoundingBox.Vertices;
                Rectangle rec = new Rectangle(box[0].X, box[0].Y, box[2].X - box[1].X, box[2].Y - box[0].Y);
                rasterImage.Crop(rec);
                using (MemoryStream memory = new MemoryStream())
                {
                    rasterImage.Save(memory);

                    await transfer.UploadAsync(
                        new TransferUtilityUploadRequest()
                        {
                            BucketName = "vvot09-faces",
                            Key = key,
                            InputStream = memory
                        });
                }
            }
        }
        
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
                    DECLARE $original_img AS Utf8;

                    UPSERT INTO faces_table (id, original_img) VALUES
                        ($id, $original_img);
                ";

            return await session.ExecuteDataQuery(
                query: query,
                txControl: TxControl.BeginSerializableRW().Commit(),
                parameters: new Dictionary<string, YdbValue>
                {
                    { "$id", YdbValue.MakeUtf8(key) },
                    { "$original_img", YdbValue.MakeUtf8(task.objectId) },
                    { "$name", YdbValue.MakeUtf8("a") },
                }
            );
        });

        response.Status.EnsureSuccess();
    }
}