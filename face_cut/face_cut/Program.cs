// See https://aka.ms/new-console-template for more information

using Function;
using Yandex.Cloud.Functions;

Environment.SetEnvironmentVariable("accessKey", "YCAJEhO4_ydYIyTosMq1WGiOZ");
Environment.SetEnvironmentVariable("secretKey", "YCPB7E1GhrAC0uSI9d6vze-qYh-k_T-eOUpqpS2w");
Environment.SetEnvironmentVariable("connectionString", "grpcs://ydb.serverless.yandexcloud.net:2135/"); 
Environment.SetEnvironmentVariable("database", "/ru-central1/b1g71e95h51okii30p25/etnhu914ov2dp4tph146");

var message = @"
{
  ""messages"": [
    {
      ""event_metadata"": {
        ""event_id"": ""c102d10d-62765f65-2bedf4a5-204de340"",
        ""event_type"": ""yandex.cloud.events.messagequeue.QueueMessage"",
        ""created_at"": ""2023-11-26T10:55:25.084Z"",
        ""tracing_context"": null,
        ""cloud_id"": ""b1g71e95h51okii30p25"",
        ""folder_id"": ""b1g8rqr6hk041qarcuvq""
      },
      ""details"": {
        ""queue_id"": ""yrn:yc:ymq:ru-central1:b1g8rqr6hk041qarcuvq:vvot09-task"",
        ""message"": {
          ""message_id"": ""c102d10d-62765f65-2bedf4a5-204de340"",
          ""md5_of_body"": ""7afd4466d563fbc113dc513f10c35cd5"",
          ""body"": ""{\""face\"":{\""BoundingBox\"":{\""Vertices\"":[{\""X\"":582,\""Y\"":220},{\""X\"":582,\""Y\"":431},{\""X\"":738,\""Y\"":431},{\""X\"":738,\""Y\"":220}]}},\""objectId\"":\""20221124_FI_3-indigenous-peoples-solidarity-fund.jpg\""}"",
          ""attributes"": {
            ""ApproximateFirstReceiveTimestamp"": ""1700996135287"",
            ""ApproximateReceiveCount"": ""50"",
            ""SenderId"": ""aje2esecoicmgf2ja4bm@as"",
            ""SentTimestamp"": ""1700996125084""
          },
          ""message_attributes"": {},
          ""md5_of_message_attributes"": """"
        }
      }
    }
  ]
}
";

var handler = new Handler();
//
// handler.FunctionHandler(message).Wait();