terraform {
  required_providers {
    yandex = {
      source = "yandex-cloud/yandex"
    }
  }
  required_version = ">= 0.13"
}

// Подключение провайдера
provider "yandex" {
  token     = var.oauth_token
  cloud_id  = var.cloud_id
  folder_id = var.folder_id
}

resource "yandex_iam_service_account" "sa" {
  folder_id = var.folder_id
  name      = "vvot09-sa"
}


// Назначение роли сервисному аккаунту
resource "yandex_resourcemanager_folder_iam_member" "sa-editor" {
  folder_id = var.folder_id
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

// Создание базы данных для имен 
resource "yandex_ydb_database_serverless" "vvot09-db-photo-face" {
  name      = "vvot09-db-photo-face"
  folder_id = var.folder_id

  deletion_protection = false

  serverless_database {
    storage_size_limit = 5
  }
}

// Создание таблицы в базе данных
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

// Создание функции определения лиц
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
    "oauth"     = var.oauth_token
    "folderId"  = var.folder_id
  }
}

// Создание триггера на вызов функции определения лиц
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

// Создание функции на создании фотографий лиц
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

// Создание триггера на обработку очереди и вызова функции создания лиц
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

// Создание шлюза
resource "yandex_api_gateway" "vvot09-apigw" {
  name        = "vvot09-apigw"
  description = "api-gateway-for-images"
  labels      = {}
  // Не получается назначить доменное имя ( lets encrypt не выдает сертификат )
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
      /{photo}:
        get:
          summary: Serve photo image from Yandex Cloud Object Storage
          parameters:
            - name: photo
              in: path
              required: true
              schema:
                type: string
          x-yc-apigateway-integration:
            type: object_storage
            bucket: vvot09-photo
            object: '{photo}'
            service_account_id: ${yandex_iam_service_account.sa.id}

  EOT
}

// Создание функции для телеграм бота
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
    "botToken"         = var.botToken
  }
}

// Сделать функцию тг бота публичной
resource "yandex_function_iam_binding" "boot-iam" {
  function_id = yandex_function.vvot09-boot.id
  role        = "serverless.functions.invoker"
  members = [
    "system:allUsers",
  ]
}

// lets encrypt не выдает сертификат =(
# resource "yandex_cm_certificate" "cert" {
#   name    = "cert"
#   domains = ["vvot09-apigw.ru"]

#   managed {
#     challenge_type = "DNS_CNAME"
#   }
# }

// Назначить обработчик телеграм боту в виде функции телеграм бота
data "http" "webhook" {
  url = "https://api.telegram.org/bot${var.botToken}/setWebhook?url=https://functions.yandexcloud.net/${yandex_function.vvot09-boot.id}"
}
