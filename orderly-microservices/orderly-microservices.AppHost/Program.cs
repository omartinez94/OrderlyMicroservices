var builder = DistributedApplication.CreateBuilder(args);

// Phase 4: minimal AppHost that brings up every Orderly service. Each
// AddProject call wires a service to the AppHost's lifecycle so the
// Aspire dashboard (and any future Aspire orchestration hooks) can
// observe + control it. The references resolve to the `Projects.<Name>`
// classes the Aspire SDK generates at build time from the
// <ProjectReference> entries in the AppHost csproj.
//
// Full Aspire dashboard UX (telemetry forwarding, secret management,
// resource visualisation) is a follow-up; this commit only restores
// the project shell so the deliverable listed in
// PERSISTENCE_AND_RELIABILITY_PLAN.md §6.4 is in place.
builder.AddProject<Projects.Catalog_API>("catalog-api");
builder.AddProject<Projects.Basket_API>("basket-api");
builder.AddProject<Projects.Ordering_API>("ordering-api");
builder.AddProject<Projects.Kitchen_API>("kitchen-api");
builder.AddProject<Projects.Identity_API>("identity-api");
builder.AddProject<Projects.Discount_Grpc>("discount-grpc");
builder.AddProject<Projects.YarpApiGateway>("yarp-api-gateway");

builder.Build().Run();
