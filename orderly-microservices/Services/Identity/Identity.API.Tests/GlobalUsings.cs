// Global usings for the Identity.API.Tests project.
//
// Identity.API's own GlobalUsings.cs is NOT inherited transitively — global using
// directives are applied per-assembly. We repeat the subset this test project needs
// so individual test files can stay focused on the scenario under test.
global using FluentAssertions;
global using NSubstitute;
global using Xunit;

// Production namespaces we exercise directly.
global using BuildingBlocks.Exceptions;
global using FluentValidation;
global using Identity.API.Dtos;
global using Identity.API.Models;
global using Identity.API.Services;
global using Identity.API.Validators;
global using MediatR;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Diagnostics;
global using System.Security.Claims;

// Disambiguate the production IdentityDbContext from the base class of the same name
// in Microsoft.AspNetCore.Identity.EntityFrameworkCore. All tests use the concrete
// Identity.API.Data.IdentityDbContext (the one with the UserRestaurants / Permissions /
// RolePermissions / LoginAuditLogs DbSets the handlers depend on).
global using IdentityDbContext = Identity.API.Data.IdentityDbContext;