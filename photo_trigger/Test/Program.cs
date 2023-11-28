// See https://aka.ms/new-console-template for more information

using Function;

var handler = new Handler();


var message = @"{
  ""messages"": [
    {
      ""event_metadata"": {
        ""event_id"": ""bb1dd06d-a82c-49b4-af98-d8e0c5a1d8f0"",
        ""event_type"": ""yandex.cloud.events.storage.ObjectDelete"",
        ""created_at"": ""2019-12-19T14:17:47.847365Z"",
        ""tracing_context"": {
          ""trace_id"": ""dd52ace79c62892f"",
          ""span_id"": """",
          ""parent_span_id"": """"
        },
        ""cloud_id"": ""b1gvlrnlei4l5idm9cbj"",
        ""folder_id"": ""b1g88tflru0ek1omtsu0""
      },
      ""details"": {
        ""bucket_id"": ""vvot09-photo"",
        ""object_id"": ""20221124_FI_3-indigenous-peoples-solidarity-fund.jpg""
      }
    }
  ]
}";

try
{
  handler.FunctionHandler(message).Wait();
}
catch (Exception e)
{
  Console.WriteLine(e);
  Console.ReadKey();
  throw;
}

Console.ReadKey();