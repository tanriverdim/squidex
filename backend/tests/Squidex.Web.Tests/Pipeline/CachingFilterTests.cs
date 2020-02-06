﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Squidex.Web.Pipeline
{
    public class CachingFilterTests
    {
        private readonly IHttpContextAccessor httpContextAccessor = A.Fake<IHttpContextAccessor>();
        private readonly HttpContext httpContext = new DefaultHttpContext();
        private readonly ActionExecutingContext executingContext;
        private readonly ActionExecutedContext executedContext;
        private readonly CachingOptions cachingOptions = new CachingOptions();
        private readonly CachingManager cachingManager;
        private readonly CachingFilter sut;

        public CachingFilterTests()
        {
            A.CallTo(() => httpContextAccessor.HttpContext)
                .Returns(httpContext);

            cachingManager = new CachingManager(httpContextAccessor);

            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var actionFilters = new List<IFilterMetadata>();

            executingContext = new ActionExecutingContext(actionContext, actionFilters, new Dictionary<string, object>(), this);
            executedContext = new ActionExecutedContext(actionContext, actionFilters, this)
            {
                Result = new OkResult()
            };

            sut = new CachingFilter(cachingManager, Options.Create(cachingOptions));
        }

        [Fact]
        public async Task Should_not_append_etag_if_not_found()
        {
            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal(StringValues.Empty, httpContext.Response.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public async Task Should_not_append_etag_if_empty()
        {
            httpContext.Response.Headers[HeaderNames.ETag] = string.Empty;

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal(string.Empty, httpContext.Response.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public async Task Should_not_convert_strong_etag_if_disabled()
        {
            cachingOptions.StrongETag = true;

            httpContext.Response.Headers[HeaderNames.ETag] = "13";

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal("13", httpContext.Response.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public async Task Should_not_convert_already_weak_tag()
        {
            httpContext.Response.Headers[HeaderNames.ETag] = "W/13";

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal("W/13", httpContext.Response.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public async Task Should_convert_strong_to_weak_tag()
        {
            httpContext.Response.Headers[HeaderNames.ETag] = "13";

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal("W/13", httpContext.Response.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public async Task Should_not_convert_empty_string_to_weak_tag()
        {
            httpContext.Response.Headers[HeaderNames.ETag] = string.Empty;

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal(string.Empty, httpContext.Response.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public async Task Should_return_304_for_same_etags()
        {
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Request.Headers[HeaderNames.IfNoneMatch] = "W/13";

            httpContext.Response.Headers[HeaderNames.ETag] = "13";

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal(304, ((StatusCodeResult)executedContext.Result).StatusCode);
        }

        [Fact]
        public async Task Should_not_return_304_for_different_etags()
        {
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Request.Headers[HeaderNames.IfNoneMatch] = "W/11";

            httpContext.Response.Headers[HeaderNames.ETag] = "13";

            await sut.OnActionExecutionAsync(executingContext, Next());

            Assert.Equal(200, ((StatusCodeResult)executedContext.Result).StatusCode);
        }

        [Fact]
        public async Task Should_append_surrogate_keys()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            cachingOptions.MaxSurrogateKeys = 2;

            await sut.OnActionExecutionAsync(executingContext, () =>
            {
                cachingManager.AddDependency(id1, 12);
                cachingManager.AddDependency(id2, 12);

                return Task.FromResult(executedContext);
            });

            Assert.Equal($"{id1} {id2}", httpContext.Response.Headers["Surrogate-Key"]);
        }

        [Fact]
        public async Task Should_not_append_surrogate_keys_if_maximum_is_exceeded()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            cachingOptions.MaxSurrogateKeys = 1;

            await sut.OnActionExecutionAsync(executingContext, () =>
            {
                cachingManager.AddDependency(id1, 12);
                cachingManager.AddDependency(id2, 12);

                return Task.FromResult(executedContext);
            });

            Assert.Equal(StringValues.Empty, httpContext.Response.Headers["Surrogate-Key"]);
        }

        [Fact]
        public async Task Should_generate_etag_from_ids_and_versions()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            await sut.OnActionExecutionAsync(executingContext, () =>
            {
                cachingManager.AddDependency(id1, 12);
                cachingManager.AddDependency(id2, 12);
                cachingManager.AddDependency(12);

                return Task.FromResult(executedContext);
            });

            Assert.True(httpContext.Response.Headers[HeaderNames.ETag].ToString().Length > 20);
        }

        [Fact]
        public async Task Should_not_generate_etag_when_already_added()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            await sut.OnActionExecutionAsync(executingContext, () =>
            {
                cachingManager.AddDependency(id1, 12);
                cachingManager.AddDependency(id2, 12);
                cachingManager.AddDependency(12);

                executedContext.HttpContext.Response.Headers[HeaderNames.ETag] = "W/20";

                return Task.FromResult(executedContext);
            });

            Assert.Equal("W/20", httpContext.Response.Headers[HeaderNames.ETag]);
        }

        private ActionExecutionDelegate Next()
        {
            return () => Task.FromResult(executedContext);
        }
    }
}
