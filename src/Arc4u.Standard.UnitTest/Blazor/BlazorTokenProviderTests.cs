﻿using Arc4u.Blazor;
using Arc4u.Caching.Memory;
using Arc4u.Dependency;
using Arc4u.OAuth2.Token;
using Arc4u.OAuth2.TokenProvider;
using Arc4u.Serializer;
using AutoFixture;
using AutoFixture.AutoMoq;
using Blazored.LocalStorage;
using FluentAssertions;
using Microsoft.JSInterop;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Arc4u.Standard.UnitTest.Blazor
{
    public class BlazorTokenProviderTests
    {
        public BlazorTokenProviderTests()
        {
            fixture = new Fixture();
            fixture.Customize(new AutoMoqCustomization());
        }

        private readonly Fixture fixture;

        [Fact]
        public void JwtSecurityTokenShould()
        {
            // arrange
            JwtSecurityToken jwt = new JwtSecurityToken("issuer", "audience", new List<Claim> { new Claim("key", "value") }, notBefore: DateTime.UtcNow.AddHours(-1), expires: DateTime.UtcNow.AddHours(1));

            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
            // act
            var jwt2 = new JwtSecurityToken(accessToken);

            // assert
            jwt2.EncodedPayload.Should().BeEquivalentTo(jwt.EncodedPayload);
        }

        [Fact]
        public async Task GetValidTokenShoud()
        {
            // Arrange
            JwtSecurityToken jwt = new JwtSecurityToken("issuer", "audience", new List<Claim> { new Claim("key", "value") }, notBefore: DateTime.UtcNow.AddHours(-1), expires: DateTime.UtcNow.AddHours(1));

            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
            var tokenInfo = new TokenInfo("Bearer", accessToken, "", DateTime.UtcNow);


            Dictionary<String, String> keySettings = new();
            keySettings.Add(TokenKeys.AuthorityKey, "http://sts");
            keySettings.Add(TokenKeys.RedirectUrl, "https://localhost:44444/");

            var mockLocalStorage = fixture.Freeze<Mock<ILocalStorageService>>();
            mockLocalStorage.Setup(p => p.GetItemAsStringAsync("token", It.IsAny<CancellationToken?>())).Returns(ValueTask.FromResult(accessToken));
            mockLocalStorage.Setup(p => p.RemoveItemAsync("token", It.IsAny<CancellationToken?>()));

            var mockContainer = fixture.Freeze<Mock<WindowInterop>>();
            mockContainer.Setup(m => m.OpenWindowAsync(It.IsAny<IJSRuntime>(), It.IsAny<ILocalStorageService>(), It.IsAny<String>(), It.IsAny<String>()));

            var mockKeyValueSettings = fixture.Freeze<Mock<IKeyValueSettings>>();
            mockKeyValueSettings.SetupGet(p => p.Values).Returns(keySettings);

            var sut = fixture.Create<BlazorTokenProvider>();

            // act
            var token = await sut.GetTokenAsync(mockKeyValueSettings.Object, null);

            // assert
            token.Should().NotBeNull();
            token.AccessToken.Should().Be(accessToken);
        }

        [Fact]
        public async Task GetTokenWithNullSettingsValuesShoud()
        {
            // Arrange
            Dictionary<String, String> keySettings = null;

            var mockKeyValueSettings = fixture.Freeze<Mock<IKeyValueSettings>>();
            mockKeyValueSettings.SetupGet(p => p.Values).Returns(keySettings);

            var sut = fixture.Create<BlazorTokenProvider>();

            // act
            var exception = await Record.ExceptionAsync(async () => await sut.GetTokenAsync(mockKeyValueSettings.Object, null));

            // assert
            exception.Should().NotBeNull();
            exception.Should().BeOfType<ArgumentNullException>();
        }

        [Fact]
        public async Task GetTokenWithNullSettingsShoud()
        {
            // Arrange
            var sut = fixture.Create<BlazorTokenProvider>();

            // act
            var exception = await Record.ExceptionAsync(async () => await sut.GetTokenAsync(null, null));

            // assert
            exception.Should().NotBeNull();
            exception.Should().BeOfType<ArgumentNullException>();
        }
    }
}
