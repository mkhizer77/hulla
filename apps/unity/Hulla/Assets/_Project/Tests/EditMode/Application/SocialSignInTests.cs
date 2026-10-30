using Hulla.Application.Services;
using Hulla.Infrastructure.Social;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>Sign-in with Apple, Google and Facebook (GDD 10.2, #38): the config and Google's PKCE flow.</summary>
    public sealed class SocialSignInTests
    {
        [Test]
        public void TheConfigReadsTheProvidersPublicIdsAndEmptyMeansOff()
        {
            var config = SocialConfig.Parse("{\"appleSignIn\":true,\"googleWebClientId\":\"web\",\"googleIosClientId\":\"\",\"facebookAppId\":\"123\"}");
            Assert.IsTrue(config.AppleSignIn);
            Assert.AreEqual("web", config.GoogleWebClientId);
            Assert.AreEqual("", config.GoogleIosClientId);
            Assert.AreEqual("123", config.FacebookAppId);
            Assert.IsFalse(SocialConfig.Parse("{}").AppleSignIn);
        }

        [Test]
        public void TheEditorOffersNoProvider()
        {
            var signIn = new NativeSocialSignIn(new SocialConfig(true, "web", "ios", "123"));
            foreach (SocialProvider provider in System.Enum.GetValues(typeof(SocialProvider)))
            {
                Assert.IsFalse(signIn.Supports(provider), provider.ToString());
            }
        }

        [Test]
        public void GooglesRedirectIsTheIosClientIdTurnedAround()
        {
            const string clientId = "123-abc.apps.googleusercontent.com";
            Assert.AreEqual("com.googleusercontent.apps.123-abc", GoogleOAuth.RedirectScheme(clientId));
            Assert.AreEqual("com.googleusercontent.apps.123-abc:/oauth2redirect", GoogleOAuth.RedirectUri(clientId));
        }

        [Test]
        public void ThePkceChallengeIsTheVerifiersHash()
        {
            // S256: base64url of SHA-256 over the verifier, unpadded (RFC 7636 4.2), checked against an independent hash.
            Assert.AreEqual("inSD6ex7M3nXdQskRosgYhDjP4T_1WMoX9vjUId7-xk", GoogleOAuth.Challenge("dBjftJeZ4CVP-mJ92K9JHTD4-0e7HkGb7XYf1jQO7BmUzTb4"));
            var verifier = GoogleOAuth.NewVerifier();
            Assert.AreEqual(64, verifier.Length);
            StringAssert.IsMatch("^[A-Za-z0-9_-]+$", verifier);
        }

        [Test]
        public void TheSignInUrlAsksForOpenIdWithTheChallenge()
        {
            var url = GoogleOAuth.SignInUrl("123-abc.apps.googleusercontent.com", "dBjftJeZ4CVP-mJ92K9JHTD4-0e7HkGb7XYf1jQO7BmUzTb4", "s1");
            StringAssert.StartsWith(GoogleOAuth.AuthorizeUrl + "?client_id=123-abc.apps.googleusercontent.com", url);
            StringAssert.Contains("code_challenge=inSD6ex7M3nXdQskRosgYhDjP4T_1WMoX9vjUId7-xk&code_challenge_method=S256", url);
            StringAssert.Contains("scope=openid%20email%20profile", url);
            StringAssert.Contains("&state=s1", url);
        }

        [Test]
        public void TheCodeIsTakenOnlyWhenTheStateMatches()
        {
            const string redirect = "com.googleusercontent.apps.123-abc:/oauth2redirect?state=s1&code=4%2F0AbC&scope=email";
            Assert.AreEqual("4/0AbC", GoogleOAuth.CodeFrom(redirect, "s1"));
            Assert.IsNull(GoogleOAuth.CodeFrom(redirect, "other"));
            Assert.IsNull(GoogleOAuth.CodeFrom("com.googleusercontent.apps.123-abc:/oauth2redirect", "s1"));
        }
    }
}
