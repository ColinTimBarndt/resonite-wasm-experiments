#[cfg(test)]
use quote::quote;
use unsynn::*;

keyword! {
    KFn = "fn";
    KPub = "pub";
}

unsynn! {
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
