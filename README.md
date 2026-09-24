# FaceMatch

Berilgan yuzni ko'plab rasmlar ichidan — shu jumladan bitta rasmda bir nechta odam bo'lgan guruh rasmlaridan ham — topuvchi servis.

- **.NET 10** (ASP.NET Core Minimal API)
- **PostgreSQL + pgvector** — rasm/yuz metama'lumotlari va 512 o'lchamli yuz vektorlari, HNSW indeks orqali tezkor qidiruv
- **MinIO** — asl rasmlar va yuz thumbnail'lari
- **FaceAiSharp (ONNX Runtime)** — SCRFD yuz aniqlash + ArcFace (ResNet100) yuz embedding'i; hammasi lokal, tashqi API yo'q

## Qanday ishlaydi

```
               ┌────────────── yuklash ──────────────┐
 rasmlar ──►  API ──► MinIO (images/…)               │
               │  └─► Postgres: images (status=Pending)
               │                                     │
               └─► navbat ──► Worker (N parallel)     │
                              ├─ MinIO'dan o'qish
                              ├─ SCRFD: har bir yuz + 5 nuqta (ko'z, burun, og'iz)
                              ├─ yuzni 112×112 ga tekislash (alignment)
                              ├─ ArcFace → 512-d vektor (L2-normallangan)
                              ├─ thumbnail → MinIO (faces/…)
                              └─ Postgres: faces(embedding vector(512)), status=Completed

 qidiruv: rasm ──► eng katta yuz (yoki tanlangan faceIndex) ──► vektor
          ──► SELECT … ORDER BY embedding <=> $q  (HNSW, cosine) WHERE o'xshashlik ≥ chegara
```

Har bir rasmdagi **har bir yuz** alohida qator sifatida indekslanadi, shuning uchun qidiruv odamni guruh rasmlarida ham topadi va natijada aynan qaysi yuz ekanligi (bounding box) qaytariladi.

## Ishga tushirish

### 1-variant: Docker Compose (hammasi birga)

```bash
cp .env.example .env        # ixtiyoriy: parollarni o'zgartiring
docker compose up -d --build
```

| Servis | Manzil |
|---|---|
| Web UI (yuklash + qidiruv) | http://localhost:8080 |
| API hujjatlari (Scalar / OpenAPI) | http://localhost:8080/scalar |
| Health | http://localhost:8080/health |
| MinIO konsol | http://localhost:9001 (minioadmin / minioadmin) |

> MinIO endi Docker Hub'da tayyor image tarqatmaydi, shu sababli `deploy/minio/Dockerfile` serverni rasmiy manba kodidan yig'adi (birinchi build ~2–3 daqiqa). Docker Hub limitiga tushsangiz, `.env` da `GO_IMAGE`/`ALPINE_IMAGE` ni `mirror.gcr.io/library/...` ga o'zgartiring.

### 2-variant: API'ni lokal ishga tushirish

```bash
docker compose up -d postgres minio
dotnet run --project src/FaceMatch.Api     # http://localhost:5255
```

Migratsiyalar va MinIO bucket ilova ishga tushganda avtomatik yaratiladi (`Database:MigrateOnStartup`).

### Demo

```bash
./scripts/download-samples.sh                          # ./samples ga ochiq namunaviy rasmlar
./scripts/bulk-upload.sh ./samples http://localhost:8080
curl -F file=@samples/obama.jpg "http://localhost:8080/api/search?topK=20"
```

Yoki brauzerda http://localhost:8080 — chap tomonda rasmlarni tashlang, o'ng tomonda qidirilayotgan odam rasmini tashlang. So'rov rasmida bir nechta odam bo'lsa, kerakli yuz ramkasini bosib qidiruvni o'sha odamga almashtirish mumkin.

## API

| Metod | Yo'l | Tavsif |
|---|---|---|
| `POST` | `/api/images` | Bir yoki ko'p rasm yuklash (multipart `files`). `?wait=true` — indekslashni kutib, yuzlar sonini qaytaradi |
| `GET` | `/api/images?page=&pageSize=&status=` | Rasmlar ro'yxati |
| `GET` | `/api/images/{id}` | Rasm va undagi yuzlar |
| `GET` | `/api/images/{id}/content` | Asl rasm (MinIO'dan stream) |
| `POST` | `/api/images/{id}/reindex` | Qayta indekslash |
| `DELETE` | `/api/images/{id}` | Rasm, yuzlar va MinIO obyektlarini o'chirish |
| `POST` | `/api/search?topK=&minSimilarity=&faceIndex=` | Rasm bo'yicha odamni qidirish (multipart `file`) |
| `GET` | `/api/faces/{id}/similar` | Indeksdagi yuzga o'xshashlarini topish |
| `GET` | `/api/faces/{id}/thumbnail` | Yuz thumbnail'i |
| `GET` | `/api/stats` | Indekslash statistikasi |

Qidiruv javobi misoli:

```json
{
  "queryFaces": [{ "index": 0, "box": { "x": 375.1, "y": 79.9, "width": 233.8, "height": 349.0 }, "confidence": 0.87 }],
  "selectedFaceIndex": 0,
  "minSimilarity": 0.4,
  "matchedImages": 4,
  "matches": [
    {
      "faceId": "…", "imageId": "…", "fileName": "obama3.jpg", "similarity": 0.7488,
      "box": { "x": 635.8, "y": 253.2, "width": 271.8, "height": 362.9 },
      "imageWidth": 1434, "imageHeight": 2333,
      "imageUrl": "/api/images/…/content", "thumbnailUrl": "/api/faces/…/thumbnail"
    }
  ]
}
```

Xatolar RFC 7807 `ProblemDetails` formatida qaytadi (masalan, rasm emas → `400 Invalid image`).

## Sozlamalar

`appsettings.json` yoki environment (`Section__Key`) orqali:

| Kalit | Default | Izoh |
|---|---|---|
| `ConnectionStrings:Postgres` | localhost | PostgreSQL (pgvector o'rnatilgan) |
| `Minio:Endpoint` / `AccessKey` / `SecretKey` / `Bucket` / `UseSsl` | localhost:9000 | MinIO |
| `FaceRecognition:DefaultMinSimilarity` | `0.4` | Moslik chegarasi (cosine). Pastroq → ko'proq topadi, lekin xato moslik xavfi oshadi |
| `FaceRecognition:DetectorMaxInputSize` | `1280` | Katta guruh rasmlaridagi mayda yuzlar uchun oshiring (sekinroq) |
| `FaceRecognition:MinFaceSize` | `20` | Bundan kichik yuzlar indekslanmaydi (embedding ishonchsiz) |
| `FaceRecognition:DetectionConfidenceThreshold` | `0.5` | Yuz aniqlash ishonchi |
| `FaceRecognition:MaxSearchResults` | `1000` | Bitta qidiruvdagi maksimal natija |
| `FaceRecognition:MaxImagePixels` | `80 000 000` | "Decompression bomb"dan himoya |
| `Processing:MaxDegreeOfParallelism` | `2` | Parallel indekslanadigan rasmlar soni |
| `Processing:MaxImageBytes` | 25 MB | Bitta fayl limiti |
| `Processing:MaxFilesPerRequest` | `500` | Bitta so'rovdagi fayllar soni |

## Sifat va tezlik (o'lchangan)

`samples/` dan yasalgan 300 ta kollaj (har birida 2–5 odam, jami **1747 yuz**), qidiruv so'rovi sifatida indeksda **yo'q** rasm ishlatildi:

| Odam | Haqiqatda bor rasmlar | Topildi | Recall | Noto'g'ri moslik |
|---|---|---|---|---|
| Obama | 246 | 246 | 100% | 0 |
| Biden | 211 | 211 | 100% | 0 |
| Rose Leslie | 158 | 158 | 100% | 0 |
| Alex Lacamoire | 141 | 141 | 100% | 0 |

- Bir odam turli rasmlarda: o'xshashlik ≈ 0.6–0.8; turli odamlar: < 0.15 — shuning uchun `0.4` chegarasi keng xavfsizlik zaxirasini beradi.
- Indekslash: ~0.9 s/rasm (CPU, 2 parallel, har rasmda o'rtacha ~6 yuz); qidiruv: ~0.3 s (asosiy vaqt so'rov rasmidagi yuzni aniqlashga ketadi, vektor qidiruv millisekundlar).

## Loyiha tuzilmasi

```
src/
  FaceMatch.Core/            domen (ImageAsset, Face), interfeyslar, DTO, sozlamalar
  FaceMatch.Infrastructure/  EF Core + pgvector, MinIO, FaceAiSharp analizatori,
                             navbat + background worker, ingestion/search servislar
  FaceMatch.Api/             Minimal API endpointlar, xatolar, health check, Web UI (wwwroot)
tests/FaceMatch.Tests/       unit, model (namunaviy rasmlar) va end-to-end (Postgres + MinIO) testlar
deploy/minio/                MinIO'ni manbadan yig'uvchi Dockerfile
scripts/                     namunalarni yuklab olish, ommaviy yuklash
```

## Testlar

```bash
dotnet test                                              # unit testlar
./scripts/download-samples.sh && dotnet test             # + haqiqiy rasmlardagi model testlari
docker compose up -d postgres minio
FACEMATCH_INTEGRATION=1 dotnet test                      # + to'liq end-to-end (alohida DB va bucket'da)
```

## Muhim dizayn qarorlari

- **Asinxron indekslash.** Yuklash tez qaytadi (`Pending`), og'ir ML ishi background worker'da. Navbat faqat tezlashtiruvchi — asosiy holat Postgres'dagi `status` ustunida; restartdan keyin tugallanmagan rasmlar avtomatik qayta navbatga qo'yiladi. Rasm `UPDATE … WHERE status='Pending'` bilan atomar "band qilinadi", shuning uchun ikki worker bitta rasmni bir vaqtda ishlamaydi. (Startdagi tiklash `Processing` holatini `Pending` ga qaytaradi — bu bitta API instansi uchun mo'ljallangan; gorizontal masshtablashda lease/heartbeat qo'shish kerak.)
- **Idempotentlik va dublikatlar.** SHA-256 bo'yicha unique indeks: bir xil rasm ikki marta saqlanmaydi. Qayta indekslash yuzlarni bitta tranzaksiyada almashtiradi.
- **Qayta urinish.** Vaqtinchalik xatolarda `Processing:MaxAttempts` martagacha qayta uriniladi; buzilgan fayllar darhol `Failed`.
- **pgvector HNSW** (`vector_cosine_ops`) + qidiruvda `hnsw.ef_search` va `hnsw.iterative_scan` — chegara bo'yicha filtrlashda ham natijalar yo'qolmaydi.
- **Alignment.** Har bir yuz ArcFace talab qilgan 5 nuqta bo'yicha tekislanadi; katta guruh rasmlarida butun rasm emas, faqat yuz atrofi qirqib olinadi.
- **EXIF orientatsiya** hisobga olinadi — telefon rasmlarida ramkalar to'g'ri joyda.

## Keyingi qadamlar (production uchun)

- Autentifikatsiya/avtorizatsiya (API key yoki JWT) va rate limiting — hozir API ochiq.
- Juda katta hajm uchun (millionlab yuz) navbatni tashqi brokerga (RabbitMQ/Kafka) ko'chirish va worker'larni alohida servisga ajratish.
- GPU uchun `Microsoft.ML.OnnxRuntime.Gpu` va aniqroq detektor (SCRFD 10G).
- Biometrik ma'lumotlar shaxsiy ma'lumot hisoblanadi: saqlash muddati, rozilik va kirish jurnali siyosatini qonunchilikka moslang.
