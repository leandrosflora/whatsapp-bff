# WhatsApp BFF

BFF em .NET 8 para integração com WhatsApp Cloud API.

O serviço recebe webhooks do WhatsApp, valida assinatura, persiste a entrega bruta em Kafka, consome essa fila de forma durável, encaminha mensagens recebidas para um Orchestrator e publica eventos canônicos de canal. Também expõe um endpoint interno para envio de mensagens outbound via WhatsApp Cloud API.

## Responsabilidades

- Verificar o webhook configurado no WhatsApp Cloud API.
- Receber webhooks de mensagens e status do WhatsApp.
- Validar assinatura `X-Hub-Signature-256` usando `WhatsApp:AppSecret`.
- Aplicar deduplicação em memória para entregas repetidas de mensagens.
- Persistir o payload bruto do webhook no tópico Kafka `channel.webhook.received` antes de processar.
- Consumir o webhook bruto do Kafka e transformar em eventos de domínio.
- Encaminhar mensagens inbound para o Orchestrator.
- Publicar eventos `channel.message.received` e `channel.message.status` no Kafka.
- Enviar mensagens outbound pela WhatsApp Cloud API.

## Arquitetura

```mermaid
flowchart LR
    WA[WhatsApp Cloud API] -->|GET verification / POST webhook| BFF[whatsapp-bff]
    BFF -->|raw webhook| K1[(Kafka: channel.webhook.received)]
    K1 -->|consumer group whatsapp-bff-webhook-consumer| BFF
    BFF -->|POST /messages| ORCH[Orchestrator]
    BFF -->|message received| K2[(Kafka: channel.message.received)]
    BFF -->|message status| K3[(Kafka: channel.message.status)]
    ORCH -->|POST /internal/messages| BFF
    BFF -->|send message| WA
```

## Stack

- .NET 8 / ASP.NET Core Minimal APIs
- Confluent.Kafka
- Swagger / OpenAPI em ambiente `Development`
- HttpClient com resilience handler para chamada ao Orchestrator
- MemoryCache para deduplicação simples de mensagens

## Endpoints

### `GET /webhooks/whatsapp`

Endpoint de verificação do webhook pelo WhatsApp.

Query params esperados:

| Parâmetro | Descrição |
|---|---|
| `hub.mode` | Deve ser `subscribe` |
| `hub.verify_token` | Deve bater com `WhatsApp:VerifyToken` |
| `hub.challenge` | Valor retornado em texto puro quando a verificação é válida |

Respostas:

| Status | Condição |
|---|---|
| `200 OK` | Token válido; retorna o `hub.challenge` |
| `403 Forbidden` | Token inválido ou payload incompleto |

### `POST /webhooks/whatsapp`

Recebe entregas do WhatsApp Cloud API.

Headers relevantes:

| Header | Descrição |
|---|---|
| `X-Hub-Signature-256` | Assinatura HMAC SHA-256 validada com `WhatsApp:AppSecret` |

Comportamento:

1. Gera `CorrelationId` para rastreabilidade.
2. Valida assinatura do webhook.
3. Desserializa o payload do WhatsApp.
4. Ignora entregas duplicadas quando todos os `message.id` já foram processados.
5. Publica o JSON bruto em `Kafka:RawWebhookReceivedTopic`.
6. Retorna sucesso apenas depois de persistir o payload no Kafka.

Respostas:

| Status | Condição |
|---|---|
| `200 OK` | Webhook aceito ou duplicado descartado |
| `400 Bad Request` | Payload inválido |
| `401 Unauthorized` | Assinatura ausente ou inválida |
| `503 Service Unavailable` | Falha ao persistir o webhook bruto no Kafka |

### `POST /internal/messages`

Endpoint interno para envio de mensagem outbound pelo WhatsApp.

Request:

```json
{
  "to": "5511999999999",
  "type": "text",
  "text": "Olá!"
}
```

Validações atuais:

- `to` é obrigatório.
- `text` é obrigatório.
- `type` existe no contrato, mas a implementação atual envia apenas texto.

Respostas:

| Status | Condição |
|---|---|
| `202 Accepted` | Mensagem enviada para a WhatsApp Cloud API; retorna `messageId` |
| `400 Bad Request` | `to` ou `text` ausente |
| `502 Bad Gateway` | Falha na chamada à WhatsApp Cloud API |

Resposta de sucesso:

```json
{
  "messageId": "wamid..."
}
```

## Fluxo inbound

1. WhatsApp chama `POST /webhooks/whatsapp`.
2. O BFF valida a assinatura `X-Hub-Signature-256`.
3. O BFF publica o payload bruto em `channel.webhook.received`.
4. `KafkaWebhookConsumerService` consome o tópico bruto.
5. O mapper converte o payload em:
   - mensagens inbound;
   - eventos de status.
6. Mensagens inbound são encaminhadas para o Orchestrator em `POST /messages`.
7. Após sucesso no Orchestrator, o BFF publica `channel.message.received`.
8. Eventos de status são publicados em `channel.message.status`.

## Garantia de processamento

O webhook HTTP só retorna sucesso após publicar o payload bruto no Kafka. Se a publicação falhar, retorna `503`, permitindo retry pelo provedor.

O consumidor Kafka usa commit manual. O offset só é commitado quando o processamento foi concluído com sucesso ou quando a mensagem é considerada inválida/irrecuperável. Em falha temporária no Orchestrator, o consumidor faz `Seek` para o mesmo offset e tenta novamente após backoff.

## Configuração

Arquivo base: `appsettings.json`.

```json
{
  "WhatsApp": {
    "PhoneNumberId": "",
    "AccessToken": "",
    "AppSecret": "",
    "VerifyToken": "",
    "GraphApiBaseUrl": "https://graph.facebook.com/v20.0"
  },
  "Orchestrator": {
    "BaseUrl": "http://localhost:8000"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "MessageReceivedTopic": "channel.message.received",
    "MessageStatusTopic": "channel.message.status",
    "RawWebhookReceivedTopic": "channel.webhook.received",
    "WebhookConsumerGroupId": "whatsapp-bff-webhook-consumer"
  }
}
```

### Variáveis sensíveis

Não versionar tokens ou secrets reais no `appsettings.json`.

Use variáveis de ambiente ou User Secrets:

```bash
dotnet user-secrets init
dotnet user-secrets set "WhatsApp:PhoneNumberId" "<phone-number-id>"
dotnet user-secrets set "WhatsApp:AccessToken" "<access-token>"
dotnet user-secrets set "WhatsApp:AppSecret" "<app-secret>"
dotnet user-secrets set "WhatsApp:VerifyToken" "<verify-token>"
```

## Execução local

Pré-requisitos:

- .NET SDK 8+
- Kafka acessível em `localhost:9092` ou ajuste de `Kafka:BootstrapServers`
- Orchestrator acessível em `Orchestrator:BaseUrl`
- Credenciais válidas do WhatsApp Cloud API para envio outbound e validação de webhook

Restaurar dependências:

```bash
dotnet restore
```

Executar:

```bash
dotnet run
```

URLs locais configuradas em `launchSettings.json`:

- HTTP: `http://localhost:5153`
- HTTPS: `https://localhost:7171`
- Swagger em desenvolvimento: `/swagger`

## Tópicos Kafka

| Tópico | Direção | Uso |
|---|---|---|
| `channel.webhook.received` | Produz e consome | Fila durável de payload bruto do webhook |
| `channel.message.received` | Produz | Evento canônico de mensagem inbound recebida |
| `channel.message.status` | Produz | Evento canônico de status de mensagem |

## Contrato com Orchestrator

O BFF encaminha mensagens inbound para o Orchestrator via:

```http
POST /messages
```

Base URL configurada em:

```json
"Orchestrator": {
  "BaseUrl": "http://localhost:8000"
}
```

A chamada usa `HttpClient` com retry configurado no ASP.NET resilience handler.

## Observabilidade

- Logs incluem `TraceId`, `SpanId` e `ParentId` via `ActivityTrackingOptions`.
- Cada webhook recebido recebe um `CorrelationId`.
- O `CorrelationId` é propagado no header da mensagem Kafka bruta.
- Logs de escopo são renderizados no console.

## Segurança

- `AccessToken`, `AppSecret` e `VerifyToken` devem ser tratados como segredo.
- A validação do webhook depende de `X-Hub-Signature-256`.
- O endpoint `/internal/messages` não tem autenticação/autorização própria nesta implementação; proteja por gateway, rede privada ou camada superior antes de produção.

## Limitações atuais

- Deduplicação é em memória; reinício da aplicação limpa o estado.
- Rastreamento de mensagens outbound conhecidas é em memória.
- O contrato outbound contém `type`, mas o envio implementado é apenas texto.
- Não há Dockerfile no repositório.
- Não há provisionamento local de Kafka no repositório.
- Não há testes automatizados versionados no repositório.

## Comandos úteis

```bash
# Build
dotnet build

# Run
dotnet run

# Swagger local
open http://localhost:5153/swagger
```

## Estrutura principal

```text
.
├── Adapters
│   ├── Inbound
│   │   ├── Http
│   │   └── Messaging
│   └── Outbound
│       ├── Http
│       ├── Messaging
│       └── Persistence
├── Application
│   ├── Ports
│   └── UseCases
├── Configuration
├── Domain
├── Program.cs
├── appsettings.json
└── whatsapp-bff.csproj
```
