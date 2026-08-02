import fitz, json
NEW="artifacts/fur-analysis/calib/out/p1-clean.pdf"
man=json.load(open("src/Flit.Infrastructure/Documents/Fur/fur-field-manifest.json",encoding="utf-8"))
V={
 "traffic_secretary_name":"STRIATTOYTTE MCPAL FUNDA","traffic_secretary_city":"FUNDA","traffic_secretary_code":"25286000",
 "processing_day":"25","processing_month":"6","processing_year":"2026","plate_letter":"IWL","plate_number":"38D",
 "requested_process_2":"X","vehicle_class_9":"X",
 "vehicle_brand":"BAJAJ","vehicle_line":"PULSAR 200","vehicle_colors":"NEGRO","vehicle_model":"2023",
 "vehicle_displacement":"200","vehicle_capacity":"2","vehicle_fuel_type_1":"X",
 "is_armored_vehicle_no":"X","is_dismantling_armor_no":"X",
 "vehicle_bodywork_type":"SIN CARROCERIA","vehicle_engine_number":"MT-12345","vehicle_chassis_number":"MD2BRYDZ8NWC12345",
 "vehicle_service_type_1":"X","vehicle_vin_number":"MD2BRYDZ8NWC12345",
 # VENDEDOR (propietario, vehicle_owner_*)
 "vehicle_owner_first_last_name":"JIMENEZ","vehicle_owner_second_last_name":"GUERRA","vehicle_owner_name":"AMOR",
 "vehicle_owner_document_type_c":"X","vehicle_owner_document_number":"1000445459",
 "vehicle_owner_address":"CRA 10 # 20-30","vehicle_owner_city":"BOGOTA","vehicle_owner_phone":"3109876543",
 "vehicle_owner_signature":"Validacion biometrica CC 1000445459\nUUID ven-001\nFirma a1b2c3\nAprob 25/06/2026 Vence 25/07/2026",
 # COMPRADOR (vehicle_buyer_*)
 "vehicle_buyer_first_last_name":"REICHERT","vehicle_buyer_second_last_name":"","vehicle_buyer_name":"STEFFEN",
 "vehicle_buyer_document_type_p":"X","vehicle_buyer_document_number":"C27WKYL7",
 "vehicle_buyer_address":"AV 68 # 45-12","vehicle_buyer_city":"MEDELLIN","vehicle_buyer_phone":"3201112233",
 "vehicle_buyer_signature":"Validacion biometrica PAS C27WKYL7\nUUID com-002\nFirma d4e5f6\nAprob 25/06/2026 Vence 25/07/2026",
}
doc=fitz.open(NEW); pg=doc[0]; red=(0.8,0,0)
def draw(f,txt):
    fs=f.get("fontSize",7); x=f["x"]; y=f["y"]
    if f["type"]=="checkbox":
        pg.insert_text((x,y+f.get("size",9)*0.85),"X",fontsize=f.get("size",9)+2,color=red,fontname="hebo"); return
    lines=txt.split("\n") if f["type"]=="multiline" else [txt]
    lh=fs*1.25
    for i,ln in enumerate(lines):
        if not ln.strip(): continue
        dx=x
        if f.get("w",0)>0 and f.get("align")!="left":
            tw=fitz.get_text_length(ln,fontname="hebo",fontsize=fs)
            dx=x+(f["w"]-tw)/2 if f.get("align")=="center" else x+f["w"]-tw
        pg.insert_text((dx,y+i*lh+fs*0.82),ln,fontsize=fs,color=red,fontname="hebo" if f.get("bold") else "helv")
for f in man["fields"]:
    if f["page"]==1 and V.get(f["id"],""): draw(f,V[f["id"]])
pg.get_pixmap(matrix=fitz.Matrix(2,2)).save("artifacts/fur-analysis/calib/preview-traspaso-p1.png")
print("wrote preview-traspaso-p1.png")
