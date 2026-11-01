using NUnit.Framework;

namespace KLTN.Client.Tests
{
    public sealed class AuthInputValidatorTests
    {
        [TestCase("player@example.com")]
        [TestCase("a.b+tag@sub.domain.vn")]
        public void PasswordReset_AcceptsValidEmail(string email)
        {
            Assert.IsTrue(AuthInputValidator.TryValidatePasswordReset(email, out string error));
            Assert.IsEmpty(error);
        }

        [TestCase(null, "Vui lòng nhập email.")]
        [TestCase("", "Vui lòng nhập email.")]
        [TestCase("   ", "Vui lòng nhập email.")]
        [TestCase("player", "Định dạng email không hợp lệ.")]
        [TestCase("player@", "Định dạng email không hợp lệ.")]
        [TestCase("player@example", "Định dạng email không hợp lệ.")]
        [TestCase("pla yer@example.com", "Định dạng email không hợp lệ.")]
        public void PasswordReset_RejectsInvalidEmail(string email, string expected)
        {
            Assert.IsFalse(AuthInputValidator.TryValidatePasswordReset(email, out string error));
            Assert.AreEqual(expected, error);
        }

        [Test]
        public void PasswordReset_RejectsTooLongEmail()
        {
            string email = new string('a', 250) + "@x.vn";

            Assert.IsFalse(AuthInputValidator.TryValidatePasswordReset(email, out string error));
            Assert.AreEqual("Email quá dài.", error);
        }

        [Test]
        public void Login_RequiresPasswordAfterValidEmail()
        {
            Assert.IsFalse(AuthInputValidator.TryValidateLogin("player@example.com", "", out string error));
            Assert.AreEqual("Vui lòng nhập mật khẩu.", error);
        }

        [Test]
        public void Login_RejectsShortPassword()
        {
            Assert.IsFalse(AuthInputValidator.TryValidateLogin("player@example.com", "12345", out _));
            Assert.IsTrue(AuthInputValidator.TryValidateLogin("player@example.com", "123456", out _));
        }

        [Test]
        public void Registration_RequiresMatchingConfirmation()
        {
            Assert.IsFalse(
                AuthInputValidator.TryValidateRegistration(
                    "player@example.com", "secret1", "secret2", out string error));
            Assert.AreEqual("Mật khẩu nhập lại không khớp.", error);
            Assert.IsTrue(
                AuthInputValidator.TryValidateRegistration(
                    "player@example.com", "secret1", "secret1", out _));
        }
    }
}
