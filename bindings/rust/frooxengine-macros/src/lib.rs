mod parser;

use proc_macro2::{Literal, Span, TokenStream};
use quote::{quote, quote_spanned};
use unsynn::TokenIter;

use crate::parser::{ExportFunction, ReturnTypes};

extern crate proc_macro;

#[proc_macro_attribute]
pub fn export_function(
    attr: proc_macro::TokenStream,
    item: proc_macro::TokenStream,
) -> proc_macro::TokenStream {
    if let Some(tok) = attr.into_iter().next() {
        let span = tok.span().into();
        return quote_spanned! {span=>
            compile_error!("unexpected argument");
        }
        .into();
    }

    let mut item_iter = TokenIter::new(TokenStream::from(item).into_iter());

    let export_fn = match ExportFunction::parse(&mut item_iter) {
        Ok(v) => v,
        Err(error) => {
            let message = string_literal(error.to_string());
            let span = item_iter
                .nth(error.pos())
                .map(|it| it.span())
                .unwrap_or_else(Span::call_site);
            return quote_spanned! {span=>
                compile_error!(#message);
            }
            .into();
        }
    };

    let ExportFunction {
        vis,
        name,
        args,
        returns,
        body,
    } = export_fn;

    let mut returns_stream = returns
        .as_ref()
        .map(|r| TokenIter::new(r.clone().into_iter()));

    let (return_types, is_tuple) = returns_stream
        .as_mut()
        .map(ReturnTypes::parse)
        .map(|rts| (rts.types, rts.tuple))
        .unwrap_or_default();

    let ret_vars = (0..return_types.len()).map(|i| ident(format!("__r{i}"), Span::call_site()));
    let ret_vars2 = ret_vars.clone();
    let ret_args = return_types.iter().enumerate().map(|(i, typ)| {
        let name = ident(format!("r{i}"), Span::call_site());
        quote! { #name: <#typ as ::frooxengine_rs::ValType>::Value }
    });

    let ret_vars = if is_tuple {
        quote!(#(#ret_vars,)*)
    } else {
        quote!(#(#ret_vars),*)
    };

    let mod_name = ident(format!("__wasm_export_{name}"), Span::call_site());
    let unspan_name = ident(name.to_string(), Span::call_site());

    let args_quote = args.iter().map(|(name, typ)| {
        quote! { #name: #typ }
    });
    let args_mapped = args.iter().map(|(name, typ)| {
        quote! { #name: <#typ as ::frooxengine_rs::ValType>::Value }
    });
    let arg_names = args.iter().map(|(name, _)| name);

    let wrap_returns = returns
        .as_ref()
        .map(|r| quote! { -> #r })
        .unwrap_or_default();

    let meta_section_name = string_literal(format!("__signature.export.{name}"));
    let meta_items = args
        .iter()
        .map(|(_, ty)| ty)
        .chain(&return_types)
        .map(|ty| {
            quote! { <#ty as ::frooxengine_rs::ValType>::MARKER }
        });
    let meta_len = Literal::usize_unsuffixed(args.len() + return_types.len());

    quote! {
        #[inline(always)]
        #vis fn #name(#(#args_quote),*) #wrap_returns {
            #body
        }

        mod #mod_name {
            use super::*;

            #[unsafe(link_section = #meta_section_name)]
            static META: [u8; 4 * #meta_len] = unsafe { ::core::mem::transmute([#(#meta_items),*]) };

            #[unsafe(no_mangle)]
            unsafe extern "C" fn #unspan_name(#(#args_mapped),*) -> ! {
                let (#ret_vars) = super::#name(#(
                    unsafe { ::frooxengine_rs::ParameterValType::make_box(#arg_names) }
                ),*);
                unsafe {
                    __return::#unspan_name(
                        #(::frooxengine_rs::ResultValType::unbox(#ret_vars2)),*
                    )
                }
            }

            mod __return {
                use super::*;

                #[link(wasm_import_module = "__export_returns")]
                unsafe extern "C" {
                    pub fn #unspan_name(#(#ret_args),*) -> !;
                }
            }
        }
    }
    .into()
}

fn string_literal(str: impl AsRef<str>) -> proc_macro2::TokenTree {
    proc_macro2::TokenTree::Literal(proc_macro2::Literal::string(str.as_ref()))
}

fn ident(name: impl AsRef<str>, span: proc_macro2::Span) -> proc_macro2::TokenTree {
    proc_macro2::TokenTree::Ident(proc_macro2::Ident::new(name.as_ref(), span))
}
