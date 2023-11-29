using System.Collections;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Yandex.Cloud;
using Yandex.Cloud.Credentials;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YaCloudKit.MQ;
using YaCloudKit.MQ.Model.Requests;
using Yandex.Cloud.Ai.Vision.V1;
using Yandex.Cloud.Generated;

namespace Function;

public class CutTask
{
    public Face face { get; set; }
    public string? objectId { get; set; }
}

public class Handler
{
    private readonly Sdk _sdk =
        new(new OAuthCredentialsProvider(Environment.GetEnvironmentVariable("oauth")));

    public async Task FunctionHandler(string message)
    {
        var accessKey = Environment.GetEnvironmentVariable("accessKey");
        var secretKey = Environment.GetEnvironmentVariable("secretKey");


        var jsonData = JsonConvert.DeserializeObject<JToken>(message)!;
        var objectId = (jsonData["messages"]![0]!["details"]?["object_id"]!).Value<string>();


        AmazonS3Client s3Client = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config { ServiceURL = "https://s3.yandexcloud.net" });

        var imageResponse = await s3Client.GetObjectAsync(
            new GetObjectRequest
            {
                BucketName = "vvot09-photo",
                Key = objectId
            });

        RepeatedField<AnalyzeResult> results;

        await using (Stream responseStream = imageResponse.ResponseStream)
        {
            byte[] b;
            using (var memoryStream = new MemoryStream())
            {
                await responseStream.CopyToAsync(memoryStream);
                b = memoryStream.ToArray();
            }

            results = AnalyzeImage(b);
        }

        YandexMqClient mqClient = new YandexMqClient(accessKey, secretKey);

        var response = await mqClient.GetQueueUrlAsync(
            new GetQueueUrlRequest()
            {
                QueueName = "vvot09-task",
            });

        var taskQueueUrl = response.QueueUrl;

        await CreateCutTasks(results[0].Results[0].FaceDetection.Faces, taskQueueUrl, objectId, mqClient);
    }

    private RepeatedField<AnalyzeResult> AnalyzeImage(byte[] image)
    {
        var aiService = new Services_Ai_Vision(_sdk);
        var face = new BatchAnalyzeRequest()
        {
            FolderId = Environment.GetEnvironmentVariable("folderId"),
            AnalyzeSpecs =
            {
                new AnalyzeSpec()
                {
                    Content = ByteString.CopyFrom(image),
                    Features = { new Feature() { Type = Feature.Types.Type.FaceDetection } }
                }
            }
        };
        var analyzeResponse = aiService.VisionService.BatchAnalyze(face);
        return analyzeResponse.Results;
    }

    private async Task CreateCutTasks(RepeatedField<Face> faceDetectionFaces,
        string queueUrl,
        string? objectId,
        YandexMqClient mqClient)
    {
        foreach (var face in faceDetectionFaces)
        {
            var message = JsonConvert.SerializeObject(
                new CutTask
                {
                    face = face,
                    objectId = objectId
                });

            await mqClient.SendMessageAsync(
                new SendMessageRequest
                {
                    DelaySeconds = 10,
                    MessageBody = message,
                    QueueUrl = queueUrl
                });
        }
    }
}