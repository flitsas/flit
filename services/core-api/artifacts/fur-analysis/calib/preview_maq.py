import fitz, json
TPL="src/Flit.Infrastructure/Documents/Fur/Templates/fur-maquinaria-p1-blank.pdf"
man=json.load(open("src/Flit.Infrastructure/Documents/Fur/fur-field-manifest.maquinaria.json",encoding="utf-8"))
V={
 "traffic_secretary_name":"STRIATTOYTTE MCPAL FUNZA","traffic_secretary_city":"FUNZA","traffic_secretary_code":"25286000",
 "processing_day":"25","processing_month":"6","processing_year":"2026","plate_letter":"MC","plate_number":"029554",
 "requested_process_2":"X",
 "vehicle_brand":"CATERPILLAR","vehicle_line":"320D","vehicle_colors":"AZUL VERDE","vehicle_model":"2015",
 "vehicle_engine_number":"J05ETA45699","vehicle_vin_number":"NA",
 # VENDEDOR (owner)
 "vehicle_owner_first_last_name":"JIMENEZ","vehicle_owner_second_last_name":"GUERRA","vehicle_owner_name":"AMOR",
 "vehicle_owner_document_type_nit":"X","vehicle_owner_document_number":"860059294",
 "vehicle_owner_address":"CRA 10 # 20-30","vehicle_owner_city":"BOGOTA","vehicle_owner_phone":"3109876543",
 "vehicle_owner_signature":"Validacion biometrica NIT 860059294\nUUID ven-001\nFirma a1b2c3\nAprob 25/06/2026 Vence 25/07/2026",
 # COMPRADOR (buyer)
 "vehicle_buyer_first_last_name":"REICHERT","vehicle_buyer_name":"STEFFEN",
 "vehicle_buyer_document_type_p":"X","vehicle_buyer_document_number":"C27WKYL7",
 "vehicle_buyer_address":"AV 68 # 45-12","vehicle_buyer_city":"MEDELLIN","vehicle_buyer_phone":"3201112233",
 "vehicle_buyer_signature":"Validacion biometrica PAS C27WKYL7\nUUID com-002\nFirma d4e5f6\nAprob 25/06/2026 Vence 25/07/2026",
}
doc=fitz.open(TPL); pg=doc[0]; red=(0.8,0,0)
def draw(f,txt):
    fs=f.get("fontSize",7); x=f["x"]; y=f["y"]
    if f["type"]=="checkbox":
        pg.insert_text((x,y+f.get("size",9)*0.85),"X",fontsize=f.get("size",9)+2,color=red,fontname="hebo"); return
    lines=txt.split("\n") if f["type"]=="multiline" else [txt]
    lh=fs*1.25
    for i,ln in enumerate(lines):
        if not ln.strip(): continue
        dx=x
        if f.get("w",0)>0 and f.get("align")=="center":
            tw=fitz.get_text_length(ln,fontname="hebo",fontsize=fs); dx=x+(f["w"]-tw)/2
        pg.insert_text((dx,y+i*lh+fs*0.82),ln,fontsize=fs,color=red,fontname="hebo" if f.get("bold",True) else "helv")
for f in man["fields"]:
    if V.get(f["id"],""): draw(f,V[f["id"]])
pg.get_pixmap(matrix=fitz.Matrix(2,2)).save("artifacts/fur-analysis/calib/preview-maq-traspaso.png")
print("wrote preview-maq-traspaso.png")
