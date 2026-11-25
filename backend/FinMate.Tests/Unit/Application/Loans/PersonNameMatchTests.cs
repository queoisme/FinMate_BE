using FinMate.Application.Loans;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Loans;

/// <summary>Khớp tên dùng chung cho chat và gợi ý từ thông báo (Phase 90, S3, Q15): dấu xét theo cả câu.</summary>
public class PersonNameMatchTests
{
    private static readonly string[] Honorifics = ["bạn", "anh", "chị", "em", "cô", "chú", "bác", "ông", "bà", "dì", "cậu"];

    [Theory]
    [InlineData("An", "Bạn An", "bạn An đã trả cho tôi 500k", true)]        // S3: tên ngắn nằm trong tên dài
    [InlineData("Hoa", "chị Hoa", "chị Hoa trả tôi 300k", true)]            // S3
    [InlineData("Hoa", "Hòa", "Hòa trả tôi 300k", false)]                   // S3: câu có dấu → so giữ dấu
    [InlineData("Hòa", "Hoa", "Hoa trả nợ 300k", false)]                    // câu có dấu ("trả", "nợ") → Hoa ≠ Hòa
    [InlineData("Bạn Bình", "ban binh", "ban binh tra 500k", true)]         // câu không dấu → so bỏ dấu
    [InlineData("Hòa", "hoa", "hoa tra 300k", true)]                        // câu không dấu → Hoa, Hòa là một
    [InlineData("Bình", "An", "An trả 500k", false)]
    public void Chat_SameName_FollowsTheSentence(string loanName, string said, string sentence, bool expected)
        => PersonNameMatch.SameName(loanName, said, sentence).Should().Be(expected);

    [Theory]
    [InlineData("Nam", "NAM TRA NO", true)]
    [InlineData("Nam", "NAMVIET TRA NO", false)]                             // cả từ, không phải một phần từ
    [InlineData("Chị Hoa", "CK CHI HOA TRA TIEN", true)]
    [InlineData("Bạn An", "NGUYEN VAN AN CHUYEN TIEN", true)]               // bỏ xưng hô đứng đầu
    [InlineData("Hoa", "HOA TRA NO", true)]                                  // Q15: không dấu khớp cả Hoa
    [InlineData("Hòa", "HOA TRA NO", true)]                                  //                 lẫn Hòa
    [InlineData("Hòa", "Hoa trả nợ", false)]                                 // có dấu → giữ dấu
    [InlineData("A", "A TRA NO", false)]                                     // tên dưới 2 ký tự không khớp
    [InlineData("Nam", null, false)]
    public void Notification_InText_IsByWholeWords(string loanName, string? text, bool expected)
        => PersonNameMatch.InText(loanName, text, Honorifics).Should().Be(expected);

    [Fact]
    public void AnHonorificAloneIsStillAName()
        => PersonNameMatch.InText("Bác", "BAC GUI TIEN", Honorifics).Should().BeTrue();
}
