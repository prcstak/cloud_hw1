terraform {
  required_providers {
    yandex = {
      source = "yandex-cloud/yandex"
    }
  }
  required_version = ">= 0.13"
}

locals {
  folder_id = "b1g8rqr6hk041qarcuvq"
}

// Подключение провайдера
provider "yandex" {
  token     = "y0_AgAAAABwYfFqAATuwQAAAADubwuME5d0zbOnQ3CxT3pw6Hc7feKYU3I"
  cloud_id  = "b1g71e95h51okii30p25"
  folder_id = local.folder_id
}

resource "yandex_iam_service_account" "sa" {
  folder_id = local.folder_id
  name      = "test"
}


// Назначение роли сервисному аккаунту
resource "yandex_resourcemanager_folder_iam_member" "sa-editor" {
  folder_id = local.folder_id
  role      = "admin"
  member    = "serviceAccount:${yandex_iam_service_account.sa.id}"
}

// Создание статического ключа доступа
resource "yandex_iam_service_account_static_access_key" "sa-static-key" {
  service_account_id = yandex_iam_service_account.sa.id
  description        = "static access key for object storage"
}

// Создание бакета для оригинальных фото
resource "yandex_storage_bucket" "vvot09-photo" {
  access_key = yandex_iam_service_account_static_access_key.sa-static-key.access_key
  secret_key = yandex_iam_service_account_static_access_key.sa-static-key.secret_key
  bucket     = "vvot09-photo"
  cors_rule {
    allowed_headers = ["*"]
    allowed_methods = ["PUT", "POST"]
    allowed_origins = ["*"]
  }
}

//создание бакета для фотографий лиц
resource "yandex_storage_bucket" "vvot09-faces" {
  access_key = yandex_iam_service_account_static_access_key.sa-static-key.access_key
  secret_key = yandex_iam_service_account_static_access_key.sa-static-key.secret_key
  bucket     = "vvot09-faces"
  cors_rule {
    allowed_headers = ["*"]
    allowed_methods = ["PUT", "POST"]
    allowed_origins = ["*"]
  }
}

// Создание очереди сообщений
resource "yandex_message_queue" "vvot09-task" {
  access_key = yandex_iam_service_account_static_access_key.sa-static-key.access_key
  secret_key = yandex_iam_service_account_static_access_key.sa-static-key.secret_key
  name       = "vvot09-task"
}

resource "yandex_ydb_database_serverless" "vvot09-db-photo-face" {
  name      = "vvot09-db-photo-face"
  folder_id = local.folder_id

  deletion_protection = true

  serverless_database {
    storage_size_limit = 5
  }
}

resource "yandex_ydb_table" "test_table" {
  path              = "faces_table"
  connection_string = yandex_ydb_database_serverless.vvot09-db-photo-face.ydb_full_endpoint

  column {
    name     = "id"
    type     = "Utf8"
    not_null = true
  }
  column {
    name     = "original_img"
    type     = "Utf8"
    not_null = false
  }
  column {
    name     = "name"
    type     = "Utf8"
    not_null = false
  }

  primary_key = ["id"]

}

resource "yandex_function" "vvot09-face-detection" {
  name              = "vvot09-face-detection"
  memory            = "128"
  user_hash         = "any_user_defined_string"
  entrypoint        = "Function.Handler"
  runtime           = "dotnet8"
  execution_timeout = 20
  content {
    zip_filename = "../photo_trigger/photo_trigger/photo_trigger.zip"
  }
  environment = {
    "accessKey" = yandex_iam_service_account_static_access_key.sa-static-key.access_key
    "secretKey" = yandex_iam_service_account_static_access_key.sa-static-key.secret_key
  }
}

resource "yandex_function_trigger" "vvot09-photo" {
  name   = "vvot09-photo"
  labels = {}

  object_storage {
    batch_cutoff = 1
    batch_size   = "1"
    create       = true
    delete       = false
    update       = false

    bucket_id = yandex_storage_bucket.vvot09-photo.id
  }

  function {
    id                 = yandex_function.vvot09-face-detection.id
    service_account_id = yandex_iam_service_account.sa.id
  }
}

resource "yandex_function" "vvot09-face-cut" {
  name               = "vvot09-face-cut"
  memory             = "128"
  user_hash          = "any_user_defined_string"
  entrypoint         = "Function.Handler"
  runtime            = "dotnet8"
  execution_timeout  = 20
  service_account_id = yandex_iam_service_account.sa.id
  content {
    zip_filename = "../face_cut/face_cut/face_cut.zip"
  }
  environment = {
    "accessKey"        = yandex_iam_service_account_static_access_key.sa-static-key.access_key
    "secretKey"        = yandex_iam_service_account_static_access_key.sa-static-key.secret_key
    "connectionString" = yandex_ydb_database_serverless.vvot09-db-photo-face.ydb_api_endpoint
    "database"         = yandex_ydb_database_serverless.vvot09-db-photo-face.database_path
  }
}

resource "yandex_function_trigger" "vvot09-task" {
  name   = "vvot09-task"
  labels = {}

  message_queue {
    queue_id           = yandex_message_queue.vvot09-task.arn
    service_account_id = yandex_iam_service_account.sa.id
    batch_size         = "1"
    batch_cutoff       = "10"
  }

  function {
    id                 = yandex_function.vvot09-face-cut.id
    service_account_id = yandex_iam_service_account.sa.id
  }
}

resource "yandex_api_gateway" "vvot09-apigw" {
  name        = "vvot09-apigw"
  description = "api-gateway-for-images"
  labels      = {}
  # custom_domains {
  #   fqdn           = "vvot09-apigw.ru"
  #   certificate_id = yandex_cm_certificate.cert.id
  # }

  spec = <<-EOT
    openapi: "3.0.0"
    info:
      version: 1.0.0
      title: Test API
    paths:
      /:
        get:
          summary: Serve face image from Yandex Cloud Object Storage
          parameters:
            - name: face
              in: query
              required: true
              schema:
                type: string
          x-yc-apigateway-integration:
            type: object_storage
            bucket: vvot09-faces
            object: '{face}'
            service_account_id: ${yandex_iam_service_account.sa.id}

  EOT
}

resource "yandex_function" "vvot09-boot" {
  name               = "vvot09-boot"
  memory             = "128"
  user_hash          = "any_user_defined_string"
  entrypoint         = "Function.Handler"
  runtime            = "dotnet8"
  execution_timeout  = 20
  service_account_id = yandex_iam_service_account.sa.id

  content {
    zip_filename = "../bot/bot/bot.zip"
  }
  environment = {
    "accessKey"        = yandex_iam_service_account_static_access_key.sa-static-key.access_key
    "secretKey"        = yandex_iam_service_account_static_access_key.sa-static-key.secret_key
    "connectionString" = yandex_ydb_database_serverless.vvot09-db-photo-face.ydb_api_endpoint
    "database"         = yandex_ydb_database_serverless.vvot09-db-photo-face.database_path
    "gatewayId"        = yandex_api_gateway.vvot09-apigw.id
  }
}

resource "yandex_function_iam_binding" "boot-iam" {
  function_id = yandex_function.vvot09-boot.id
  role        = "serverless.functions.invoker"
  members = [
    "system:allUsers",
  ]
}

resource "yandex_cm_certificate" "cert" {
  name    = "cert"
  domains = ["vvot09-apigw.ru"]

  managed {
    challenge_type = "DNS_CNAME"
  }
}

data "http" "webhook" {
  url = "https://api.telegram.org/bot6911449123:AAFoIYdoptbkzU1vXFApetQKkhMHCLp0HrA/setWebhook?url=https://functions.yandexcloud.net/${yandex_function.vvot09-boot.id}"
}
