// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using ProductConstructionService.BarViz;
using TextCopy;
using ProductConstructionService.BarViz.Code.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

string PcsApiBaseAddress = builder.HostEnvironment.IsDevelopment()
    ? "https://localhost:53180/"
    : builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(PcsApiBaseAddress) });
builder.Services.AddFluentUIComponents();
builder.Services.AddSingleton(ProductConstructionService.Client.PcsApiFactory.GetAnonymous(PcsApiBaseAddress));
builder.Services.InjectClipboard();
builder.Services.AddSingleton<UrlRedirectManager>();

await builder.Build().RunAsync();