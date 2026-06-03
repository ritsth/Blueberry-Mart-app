# 🫐 Blueberry Mart

A full-stack grocery retail platform built for multi-branch operations, serving both customers and shareholders through a role-based mobile and API experience. Designed to scale on **Google Cloud Platform** with real-time event streaming, intelligent caching, and analytical data warehousing.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Local Development](#local-development)
- [Environment Variables](#environment-variables)
- [Database Migrations](#database-migrations)
- [API Endpoints](#api-endpoints)
- [Running Tests](#running-tests)

---

## Overview

Blueberry Mart supports two user roles with distinct experiences:

| Role | Experience |
|---|---|
| **Customer** | Browse branch inventory, place pickup or delivery orders, submit photo reviews, earn loyalty points |
| **Shareholder** | Full inventory access across all branches, business analytics (revenue, top items, low-stock alerts) |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        CLIENT LAYER                                 │
│                                                                     │
│          📱 Expo (React Native)  ──  iOS / Android / Web           │
└───────────────────────────┬─────────────────────────────────────────┘
                            │  HTTPS (JWT Bearer)
┌───────────────────────────▼─────────────────────────────────────────┐
│                      GCP — CLOUD RUN                                │
│                                                                     │
│              🟢 BlueberryMart.Api  (.NET 8 Web API)                │
│         ┌──────────────────────────────────────────┐               │
│         │  Auth │ Inventory │ Orders │ Reviews │    │               │
│         │       Shareholder Analytics              │               │
│         └──────────┬────────────────┬─────────────┘               │
└────────────────────┼────────────────┼─────────────────────────────┘
                     │                │
       ┌─────────────▼──┐    ┌────────▼──────────────────┐
       │  🔴 Redis       │    │  🐘 Cloud SQL (PostgreSQL) │
       │  (Memorystore)  │    │                           │
       │                 │    │  users                    │
       │  • Inventory    │    │  branches                 │
       │    lookups      │    │  inventory                │
       │  • Session      │    │  orders + order_items     │
       │    cache        │    │  reviews                  │
       └─────────────────┘    └───────────────────────────┘
                     │
       ┌─────────────▼───────────────────────────────────┐
       │          Apache Kafka  (Confluent Cloud)         │
       │                                                  │
       │  Topics:                                         │
       │  • order.placed   → deduct inventory             │
       │  • order.status   → notify customer              │
       │  • review.created → update item rating cache     │
       │  • stock.alert    → trigger restock workflow     │
       └──────────────────────┬───────────────────────────┘
                              │  Streaming ingest
       ┌──────────────────────▼───────────────────────────┐
       │              GCP — BigQuery                      │
       │                                                  │
       │  Datasets:                                       │
       │  • orders_stream      (real-time sales)          │
       │  • inventory_events   (stock movements)          │
       │  • user_activity      (loyalty & engagement)     │
       │                                                  │
       │  Powers:  Shareholder analytics dashboard        │
       │           Revenue reports · Demand forecasting   │
       └──────────────────────────────────────────────────┘
```

### How the layers interact

```
Customer places order
        │
        ▼
  API validates stock ──► Redis cache hit? ──► YES ──► skip DB read
        │                         │
        │                        NO
        │                         ▼
        │               Cloud SQL (source of truth)
        │
        ▼
  Order written to Cloud SQL
        │
        ▼
  Kafka event: order.placed
        │
        ├──► Consumer: deduct inventory → update Redis + Cloud SQL
        ├──► Consumer: stream event → BigQuery (orders_stream)
        └──► Consumer: send confirmation → push notification
```

---

## Tech Stack

### Backend
| Layer | Technology |
|---|---|
| API | .NET 8 Web API (C#) |
| Auth | JWT Bearer — role-based (`Customer` / `Shareholder`) |
| ORM | Entity Framework Core 8 + Npgsql |
| Primary DB | PostgreSQL (Cloud SQL on GCP) |
| Cache | Redis (GCP Memorystore) |
| Message Broker | Apache Kafka (Confluent Cloud) |
| Analytics | Google BigQuery |
| Hosting | GCP Cloud Run (containerised, auto-scaling) |
| Storage | GCP Cloud Storage (review images) |

### Frontend
| Layer | Technology |
|---|---|
| Framework | Expo (React Native) — iOS, Android, Web |
| Navigation | React Navigation (Native Stack) |
| Auth Storage | AsyncStorage |
| HTTP | Fetch API |

### Infrastructure
| Concern | Tool |
|---|---|
| Container registry | GCP Artifact Registry |
| CI/CD | GitHub Actions → Cloud Run deploy |
| Secrets | GCP Secret Manager |
| Monitoring | GCP Cloud Logging + Cloud Monitoring |

---

## Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 14+](https://www.postgresql.org/download/)
- [Node.js 18+](https://nodejs.org/) + npm
- [Expo CLI](https://docs.expo.dev/get-started/installation/)
- `psql` — install via `brew install libpq`

### Backend

```bash
# 1. Copy and fill in your local secrets
cp BlueberryMart.Api/appsettings.Development.example.json \
   BlueberryMart.Api/appsettings.Development.json

# 2. Apply database migrations
PGPASSWORD=your_password psql -h localhost -U postgres -d blueberry_mart \
  -f Database/Migrations/01_InitSchema.sql
PGPASSWORD=your_password psql -h localhost -U postgres -d blueberry_mart \
  -f Database/Migrations/02_AddReviews.sql
PGPASSWORD=your_password psql -h localhost -U postgres -d blueberry_mart \
  -f Database/Migrations/03_AddOrderItems.sql

# 3. Run the API (seeds mock data on first start)
dotnet run --project BlueberryMart.Api
# Swagger UI → http://localhost:5027/swagger
```

### Frontend

```bash
cd BlueberryMartApp
cp .env.example .env.local          # set EXPO_PUBLIC_API_URL
npm install
npx expo start
```

---

## Environment Variables

### Backend

Sensitive values are **never committed**. Set them in `appsettings.Development.json` locally, or as environment variables in production (double-underscore maps to colon in .NET).

| Variable | Description |
|---|---|
| `Jwt__Secret` | HMAC-SHA256 signing key — minimum 32 characters |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |

### Frontend

| Variable | Description |
|---|---|
| `EXPO_PUBLIC_API_URL` | Base URL of the .NET API (`http://localhost:5027` locally) |

---

## Database Migrations

Migrations are plain SQL files applied in order. They are **never run automatically** — apply them manually or via a deploy script.

```bash
psql "$CONNECTION_STRING" -f Database/Migrations/01_InitSchema.sql
psql "$CONNECTION_STRING" -f Database/Migrations/02_AddReviews.sql
psql "$CONNECTION_STRING" -f Database/Migrations/03_AddOrderItems.sql
```

---

## API Endpoints

All endpoints except `/api/auth/login` require a `Bearer` token.

| Method | Path | Role | Description |
|---|---|---|---|
| `POST` | `/api/auth/login` | — | Returns a signed JWT |
| `GET` | `/api/inventory/customer?branchId=` | Customer | In-stock, non-bulk items for a branch |
| `GET` | `/api/inventory/shareholder` | Shareholder | Full inventory across all branches |
| `POST` | `/api/orders` | Customer | Place an order, deducts stock, credits loyalty points |
| `POST` | `/api/reviews` | Customer | Submit a text or photo review |
| `GET` | `/api/shareholders/analytics` | Shareholder | Revenue, top items, low-stock alerts |

---

## Running Tests

Integration tests run against a dedicated `blueberry_mart_test` PostgreSQL database that is created and torn down automatically.

```bash
dotnet test Tests/BlueberryMart.Api.Tests
```

**21 tests** covering auth, inventory access control, order validation, review submission, and analytics.
