# Third-party components

The included license and notice files apply to their respective components.

| Component | Version | Upstream / license |
|---|---|---|
| .NET / Windows Desktop Runtime | 10.0.12 | https://github.com/dotnet/runtime and https://github.com/dotnet/wpf ; MIT plus included third-party notices |
| RapidOcrNet | 4.2.0 | https://github.com/BobLd/RapidOcrNet/tree/708cae2fcb88720e1d891a81b5ee3e8b2bcc139e ; Apache-2.0 |
| ONNX Runtime | 1.29.0 | https://github.com/microsoft/onnxruntime ; MIT and third-party notices |
| SkiaSharp | 3.119.1 | https://github.com/mono/SkiaSharp ; MIT and third-party notices |
| Clipper2 | 2.0.0 | https://github.com/AngusJohnson/Clipper2 ; Boost Software License 1.0 |
| System.Numerics.Tensors | 9.0.0 | https://github.com/dotnet/runtime ; MIT |
| PaddleOCR / PP-OCRv5 mobile models | v5 | https://github.com/PaddlePaddle/PaddleOCR ; Apache-2.0 |
| RapidOCR ONNX conversions | model collection v3.9.2 | https://github.com/RapidAI/RapidOCR ; Apache-2.0 |

## Exact model assets

Models downloaded from the RapidAI/RapidOCR ModelScope repository, pinned to v3.9.2:

- `det.onnx`: https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx
- `rec.onnx`: https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx
- `cls.onnx`: https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx
- `dict.txt`: https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/ppocr/utils/dict/ppocrv5_dict.txt (the exact downloaded dictionary is bundled).

SHA-256:

```
det.onnx  4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae
rec.onnx  5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5
cls.onnx  54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7
dict.txt  d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b
```

The orientation model is bundled for the engine initialization contract; angle classification is disabled for ordinary desktop text. No GPU runtime or cloud OCR is included.
