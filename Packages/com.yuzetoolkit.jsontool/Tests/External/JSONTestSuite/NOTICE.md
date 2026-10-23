# JSONTestSuite fixtures

The `test_parsing` corpus and `LICENSE` in this directory are copied from
[`nst/JSONTestSuite`](https://github.com/nst/JSONTestSuite) at commit
`1ef36fa01286573e846ac449e8683f8833c5b26a`.

The upstream corpus is distributed under the MIT License. Files prefixed with `y_` must be
accepted, files prefixed with `n_` must be rejected, and files prefixed with `i_` represent
implementation-defined interoperability cases. Yuze JSON Tool runs the required accept/reject
sets as conformance gates and keeps explicit package-owned tests for its chosen `i_` policies.
