#[cfg(test)]
use quote::quote;
use unsynn::*;

keyword! {
    KExtern = "extern";
    KFn = "fn";
    KPub = "pub";
    Ki8 = "i8";
    Ku8 = "u8";
    Ki16 = "i16";
    Ku16 = "u16";
    Ki32 = "i32";
    Ku32 = "u32";
    Ki64 = "i64";
    Ku64 = "u64";
    Kf32 = "f32";
    Kf64 = "f64";
}

unsynn! {
    enum WasmFnType {
        I8(Ki8),
        U8(Ku8),
        I16(Ki16),
        U16(Ku16),
        I32(Ki32),
        U32(Ku32),
        I64(Ki64),
        U64(Ku64),
        F32(Kf32),
        F64(Kf64),
        Extern(KExtern),
    }

    struct ExportFn {
        vis: Option<Vis>,
        _fn: KFn,
        name: Ident,
        args: ParenthesisGroupContaining<FnArgList>,
        returns: Option<FnRet>,
        body: BraceGroup,
    }

    struct FnArg {
        name: Ident,
        _colon: Skip<PunctAlone<':'>>,
        typ: VerbatimUntil<Comma>,
    }

    struct FnRet {
        _arrow: Skip<RArrow>,
        result: VerbatimUntil<BraceGroup>,
    }

    struct AngleTokenTree(
        #[allow(clippy::type_complexity)]
        pub Either<Cons<Lt, Vec<Cons<Except<Gt>, AngleTokenTree>>, Gt>, TokenTree>,
    );

    struct Vis {
        _pub: KPub,
        _opt: Option<Skip<ParenthesisGroup>>,
    }
}

type FnArgList = CommaDelimitedVec<FnArg>;

pub struct ExportFunction {
    pub vis: TokenStream,
    pub name: Ident,
    pub args: Vec<(Ident, TokenStream)>,
    pub returns: Option<TokenStream>,
    pub body: TokenStream,
}

type VerbatimUntil<C> = Many<Cons<Except<C>, AngleTokenTree>>;

impl ExportFunction {
    pub fn parse(tokens: &mut TokenIter) -> Result<Self> {
        let result = ExportFn::parse_all(tokens)?;

        Ok(Self {
            vis: result
                .vis
                .map(|v| v.into_token_stream())
                .unwrap_or_default(),
            name: result.name,
            args: result
                .args
                .content
                .into_iter()
                .map(|tok| (tok.value.name, tok.value.typ.into_token_stream()))
                .collect(),
            returns: result.returns.map(|it| it.result.into_token_stream()),
            body: result.body.0.stream(),
        })
    }
}

#[test]
fn test_fn_arg_list() {
    let mut tokens = TokenIter::new(quote! {(a: b<'a>, c: asdf::check)}.into_iter());

    let result = ParenthesisGroupContaining::<FnArgList>::parse_all(&mut tokens);

    result.unwrap();
}

#[test]
fn test_fn_no_ret() {
    let mut tokens = TokenIter::new(quote! {fn x(a: b) {}}.into_iter());

    let result = ExportFn::parse_all(&mut tokens);

    result.unwrap();
}

#[test]
fn test_fn_ret() {
    let mut tokens = TokenIter::new(quote! {fn x(a: b) -> c {}}.into_iter());

    let result = ExportFn::parse_all(&mut tokens);

    result.unwrap();
}

type TupleParser = ParenthesisGroupContaining<CommaDelimitedVec<VerbatimUntil<Comma>>>;

#[derive(Default)]
pub struct ReturnTypes {
    pub types: Vec<TokenStream>,
    pub tuple: bool,
}

impl ReturnTypes {
    pub fn parse(tokens: &mut TokenIter) -> Self {
        match TupleParser::parse_all(tokens) {
            Err(_) => Self {
                types: vec![tokens.clone().into_token_stream()],
                tuple: false,
            },
            Ok(tuple) => Self {
                types: tuple
                    .content
                    .into_iter()
                    .map(|tok| tok.value.into_token_stream())
                    .collect(),
                tuple: true,
            },
        }
    }
}
