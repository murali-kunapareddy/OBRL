using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Obrl.Web.Localization;

/// <summary>
/// Gives validation attributes that have no message of their own an English message that is also a resource key,
/// so data annotations localization translates it like any other text.
/// </summary>
public sealed class DefaultValidationMessages : IValidationMetadataProvider
{
    public const string Required = "The {0} field is required.";
    public const string MaxLength = "{0} can be at most {1} characters.";
    public const string LengthRange = "{0} must be between {2} and {1} characters.";
    public const string Email = "{0} is not a valid email address.";
    public const string Phone = "{0} is not a valid phone number.";
    public const string Range = "{0} must be between {1} and {2}.";
    public const string Compare = "{0} and {1} do not match.";

    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if (attribute.ErrorMessage is not null || attribute.ErrorMessageResourceName is not null) continue;
            attribute.ErrorMessage = attribute switch
            {
                RequiredAttribute => Required,
                StringLengthAttribute s => s.MinimumLength > 0 ? LengthRange : MaxLength,
                MaxLengthAttribute => MaxLength,
                EmailAddressAttribute => Email,
                PhoneAttribute => Phone,
                RangeAttribute => Range,
                CompareAttribute => Compare,
                _ => null,
            };
        }
    }
}

/// <summary>Translates model binding's own messages, such as a number field that received text.</summary>
public sealed class LocalizedMvcOptions(IStringLocalizerFactory factory) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        var L = factory.Create(typeof(SharedResource));
        var m = options.ModelBindingMessageProvider;
        m.SetValueIsInvalidAccessor(v => L["The value '{0}' is not valid.", v]);
        m.SetAttemptedValueIsInvalidAccessor((v, f) => L["The value '{0}' is not valid for {1}.", v, f]);
        m.SetMissingBindRequiredValueAccessor(f => L["The {0} field is required.", f]);
        m.SetMissingKeyOrValueAccessor(() => L["A value is required."]);
        m.SetValueMustNotBeNullAccessor(f => L["The {0} field is required.", f]);
        m.SetValueMustBeANumberAccessor(f => L["{0} must be a number.", f]);
        m.SetNonPropertyValueMustBeANumberAccessor(() => L["This field must be a number."]);
        m.SetUnknownValueIsInvalidAccessor(f => L["The value is not valid for {0}.", f]);
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(v => L["The value '{0}' is not valid.", v]);
        m.SetNonPropertyUnknownValueIsInvalidAccessor(() => L["The value is not valid."]);
        m.SetMissingRequestBodyRequiredValueAccessor(() => L["A value is required."]);
        options.ModelMetadataDetailsProviders.Add(new DefaultValidationMessages());
    }
}
