namespace Basket.API.Payment;

/// <summary>
/// Helpers for deriving the redacted <c>PaymentMethodSummary</c>
/// fields (Brand, LastFour) from the raw card number carried on the
/// <c>BasketCheckoutDto</c>. The wire carries only
/// the redacted summary — full PAN and CVV stay inside Basket.
/// </summary>
public static class CardSummaryDerivation
{
    /// <summary>
    /// Derives the card brand from the leading digit(s) of the card
    /// number. ISO/IEC 7812 issuer identification numbers:
    /// <list type="bullet">
    /// <item>4 → Visa</item>
    /// <item>5 → Mastercard (also 2-series in the BIN range, post-2017)</item>
    /// <item>3 → American Express (also 7, the airline partner range)</item>
    /// <item>6 → Discover</item>
    /// </list>
    /// Empty / non-numeric / unrecognised inputs return
    /// <c>"Unknown"</c>.
    /// </summary>
    public static string DeriveCardBrand(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return "Unknown";
        }

        var firstChar = cardNumber.TrimStart()[0];
        return firstChar switch
        {
            '4' => "Visa",
            '5' or '2' => "Mastercard",
            '3' or '7' => "Amex",
            '6' => "Discover",
            _ => "Unknown",
        };
    }

    /// <summary>
    /// Returns the last 4 digits of the card number as a string. Strips
    /// non-digit characters (spaces, dashes) before slicing. Returns
    /// <c>"0000"</c> for empty / non-numeric inputs so the wire payload
    /// always carries a defined string.
    /// </summary>
    public static string ExtractLastFour(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return "0000";
        }

        var digitsOnly = new string(cardNumber.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length < 4)
        {
            return "0000";
        }

        return digitsOnly[^4..];
    }
}
