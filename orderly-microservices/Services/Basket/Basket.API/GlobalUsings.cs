global using Basket.API.Behaviors;
global using Basket.API.Data;
global using Basket.API.Dtos;
global using Basket.API.Endpoints;
global using Basket.API.Exceptions;
// Phase 2 promotion (per §0.3.1, 2+ files): Basket.API.Messaging hosts
// the CheckoutBasketOutboxMessage document and the dispatcher
// BackgroundService; both CheckoutBasketCommandHandler and Program.cs
// resolve types from this namespace.
global using Basket.API.Messaging;
global using BuildingBlocks.Authorization;
global using BuildingBlocks.Behaviors;
global using BuildingBlocks.CQRS;
global using BuildingBlocks.Exceptions;
global using BuildingBlocks.Exceptions.Handler;
global using BuildingBlocks.Multitenancy;
// Phase 2 promotion (per §0.3.1, 2+ files): BuildingBlocks.Messaging.Outbox
// exposes OutboxOptions, which the dispatcher and Program.cs both resolve.
global using BuildingBlocks.Messaging.Outbox;
global using Carter;
global using FluentValidation;
global using Mapster;
global using Marten;
global using MediatR;
global using Microsoft.AspNetCore.Http;
global using Microsoft.Extensions.Caching.Distributed;
global using NodaTime;
global using System.Security.Claims;
global using Models = global::Basket.API.Models;
