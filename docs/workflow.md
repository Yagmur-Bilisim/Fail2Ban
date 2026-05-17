# AI İş Akışı (Workflow)

Bu proje üzerinde çalışacak AI, aşağıdaki adımları sırasıyla izlemelidir:

1.  **Analiz:** Mevcut `.NET API` endpoint'lerini ve `Electron` konfigürasyonunu oku.
2.  **Redis Senkronizasyonu:** Başlamadan önce Redis'teki son durumu (`last_state`) kontrol et.
3.  **UI Geliştirme:** Tailwind CSS kullanarak premium bileşenler oluştur (Bkz: `skills.md`).
4.  **Mantık Kurulumu:** BrowserView nesnelerini oluştur ve API'den gelen yetkilere göre kısıtla.
5.  **Test ve Log:** Yapılan her değişikliği hem konsola hem de Redis'e logla.
6.  **Commit:** İşlem bittiğinde Redis'e özet geç ve yapılanları commit mesajına ekle.
