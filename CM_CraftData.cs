﻿using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

using KatLib;

namespace CraftManager
{

    public class CraftDataCache
    {


        public string cache_path = Paths.joined(KSPUtil.ApplicationRootPath, "GameData", "CraftManager", "craft_data.cache");
        public Dictionary<string, ConfigNode> data = new Dictionary<string, ConfigNode>();



        public CraftDataCache(){
            if(File.Exists(cache_path)){
                load(); 
            }
        }

        //takes a CraftData craft and creates a ConfigNode that contains all of it's public properties, ConfigNodes is held in 
        //a <string, ConfigNode> dict with the full path as the key. 
        public void write(CraftData craft){
            ConfigNode node = new ConfigNode();
            foreach(var prop in craft.GetType().GetProperties()){               
                node.AddValue(prop.Name, prop.GetValue(craft, null));
            }
            if(data.ContainsKey(craft.path)){
                data[craft.path] = node;
            }else{
                data.Add(craft.path,node);
            }
            save();
        }

        //Takes a CraftData craft object and if the cached data contains a matching path AND the checksum value matches
        //then the craft's properties are populated from the ConfigNode in the cache.  Returns true if matching data was
        //found, otherwise returns false, in which case the data will have to be interpreted from the .craft file.
        public bool try_fetch(CraftData craft){
            if(data.ContainsKey(craft.path) && data[craft.path].GetValue("checksum") == craft.checksum){
                try{
                    ConfigNode node = data[craft.path];                    
                    foreach(var prop in craft.GetType().GetProperties()){               
                        if(prop.CanWrite){
                            var node_value = node.GetValue(prop.Name);
                            if(!String.IsNullOrEmpty(node_value)){
                                var type = prop.GetValue(craft, null);
                                if(type is float){
                                    prop.SetValue(craft, float.Parse(node_value), null);                                
                                }else if(type is int){
                                    prop.SetValue(craft, int.Parse(node_value), null);                                
                                }else if(type is bool){
                                    prop.SetValue(craft, bool.Parse(node_value), null);                                
                                }else{
                                    prop.SetValue(craft, node_value, null);                                
                                }

                            }
                        }
                    }

                    return true;
                }

                catch(Exception e){
                    CraftManager.log("try_fetch failed: " + e.Message + "\n" + e.StackTrace);
                    return false;
                }

            } else{
                return false;
            }

        }

        private void save(){
            ConfigNode nodes = new ConfigNode();
            ConfigNode craft_nodes = new ConfigNode();

            foreach(KeyValuePair<string, ConfigNode> pair in data){
                craft_nodes.AddNode("CRAFT", pair.Value);
            }
            nodes.AddNode("CraftData", craft_nodes);

            nodes.Save(cache_path);
        }

        private void load(){
            data.Clear();
            ConfigNode nodes = ConfigNode.Load(cache_path);
            ConfigNode craft_nodes = nodes.GetNode("CraftData");
            foreach(ConfigNode node in craft_nodes.nodes){
                data.Add(node.GetValue("path"), node);
            }
        }
    }


    public class CraftData
    {
        //**Class Methods/Variables**//

        public static List<CraftData> all_craft = new List<CraftData>();  //will hold all the craft loaded from disk
        public static List<CraftData> filtered  = new List<CraftData>();  //will hold the results of search/filtering to be shown in the UI.
        public static Dictionary<string, AvailablePart> game_parts = new Dictionary<string, AvailablePart>();  //populated on first use, name->part lookup for installed parts
        public static CraftDataCache cache = null;



        public static void load_craft(){            
            if(cache == null){
                cache = new CraftDataCache();                
            }

            string[] craft_file_paths;
            craft_file_paths = Directory.GetFiles(Paths.joined(KSPUtil.ApplicationRootPath, "saves"), "*.craft", SearchOption.AllDirectories);

            all_craft.Clear();
            foreach(string path in craft_file_paths){
                all_craft.Add(new CraftData(path));
            }
        }


        public static void filter_craft(Dictionary<string, object> criteria){
            filtered = all_craft;    
            if(criteria.ContainsKey("save_dir")){
                filtered = filtered.FindAll(craft => craft.save_dir == (string)criteria["save_dir"]);
            }
            if(criteria.ContainsKey("search")){
                filtered = filtered.FindAll(craft => craft.name.ToLower().Contains(((string)criteria["search"]).ToLower()));
            }
            if(criteria.ContainsKey("type")){
                Dictionary<string, bool> types = (Dictionary<string, bool>) criteria["type"];
                List<string> selected_types = new List<string>();
                foreach(KeyValuePair<string, bool> t in types){
                    if(t.Value){                        
                        selected_types.Add(t.Key=="Subassemblies" ? "Subassembly" : t.Key);
                    }
                }                                   
                filtered = filtered.FindAll(craft => selected_types.Contains(craft.construction_type));
            }
            if(criteria.ContainsKey("tags")){
                List<string> s_tags = (List<string>)criteria["tags"];
                if((bool)criteria["tag_mode_reduce"]){
                    foreach(string tag in s_tags){
                        filtered = filtered.FindAll(craft => craft.tags().Contains(tag));
                    }
                } else{
                    filtered = filtered.FindAll(craft =>{
                        bool sel = false;
                        foreach(string tag in craft.tags()){
                            if(s_tags.Contains(tag)){
                                sel = true;
                            }
                        }
                        return sel;
                    });
                }
            }
            if(criteria.ContainsKey("sort")){
                string sort_by = (string)criteria["sort"];
                filtered.Sort((x,y) => {
                    //{"name", "part_count", "mass", "date_created", "date_updated", "stage_count"};
                    if(sort_by == "name"){
                        return x.name.CompareTo(y.name);
                    }else if(sort_by == "part_count"){
                        return y.part_count.CompareTo(x.part_count);
                    }else if(sort_by == "stage_count"){
                        return y.stage_count.CompareTo(x.stage_count);
                    }else if(sort_by == "mass"){
                        return y.mass["total"].CompareTo(x.mass["total"]);
                    }else if(sort_by == "date_created"){
                        return x.create_time.CompareTo(y.create_time);
                    }else if(sort_by == "date_updated"){
                        return x.last_updated_time.CompareTo(y.last_updated_time);
                    }else{
                        return x.name.CompareTo(y.name);
                    }
                });
                if(criteria.ContainsKey("reverse_sort") && (bool)criteria["reverse_sort"]){
                    filtered.Reverse();
                }
            }
        }
            
        public static void select_craft(CraftData craft){
            foreach(CraftData list_craft in filtered){
                list_craft.selected = list_craft == craft;
            }
        }

        public static CraftData selected_craft(){                    
            return filtered.Find(c => c.selected == true);
        }



        //**Instance Methods/Variables**//

        //craft attributes - these attributes will either be loaded from a .craft file or from the cache
        public string path { get; set; }
        public string checksum { get; set; }
        public string name { get; set; }
        public string alt_name { get; set; }
        public string description { get; set; }
        public string construction_type { get; set; }
        public bool missing_parts { get; set; }
        public bool locked_parts { get; set; }
        public int stage_count { get; set; }
        public int part_count { get; set; }
        public Dictionary<string, float> cost = new Dictionary<string, float> {
            {"dry", 0.0f}, {"fuel", 0.0f}, {"total", 0.0f}
        };
        public Dictionary<string, float> mass = new Dictionary<string, float> {
            {"dry", 0.0f}, {"fuel", 0.0f}, {"total", 0.0f}
        };

        public float cost_dry{ 
            get { return cost["dry"]; } 
            set { cost["dry"] = value; }
        }
        public float cost_fuel{ 
            get { return cost["fuel"]; } 
            set { cost["fuel"] = value; }
        }
        public float cost_total{ 
            get { return cost["total"]; } 
            set { cost["total"] = value; }
        }
        public float mass_dry{ 
            get { return mass["dry"]; } 
            set { mass["dry"] = value; }
        }
        public float mass_fuel{ 
            get { return mass["fuel"]; } 
            set { mass["fuel"] = value; }
        }
        public float mass_total{ 
            get { return mass["total"]; } 
            set { mass["total"] = value; }
        }


        //Attribues which are always set from craft file/path, never loaded from cache
        public Texture thumbnail;
        public string create_time;
        public string last_updated_time;
        public string save_dir;
        public bool selected = false;


        //Initialize a new CraftData object. Takes a path to a .craft file and either populates it from attributes from the craft file
        //or loads information from the CraftDataCache
        public CraftData(string full_path){
            path = full_path;
            checksum = Checksum.digest(File.ReadAllText(path));

            //attempt to load craft data from the cache. If unable to fetch from cache then load 
            //craft data from the .craft file and cache the loaded info.
            if(!cache.try_fetch(this)){
                read_craft_info_from_file();
                cache.write(this);                
            }

            //set timestamp data from the craft file
            create_time = System.IO.File.GetCreationTime(path).ToBinary().ToString();
            last_updated_time = System.IO.File.GetLastWriteTime(path).ToBinary().ToString();

            save_dir = path.Replace(Paths.joined(KSPUtil.ApplicationRootPath, "saves", ""), "").Split('/')[0];
            thumbnail = ShipConstruction.GetThumbnail("/thumbs/" + save_dir + "_" + construction_type + "_" + name);
        }


        //Parse .craft file and read info
        private void read_craft_info_from_file(){
            name = Path.GetFileNameWithoutExtension(path);
            CraftManager.log("Loading craft data from file for " + name);

            ConfigNode data = ConfigNode.Load(path);
            ConfigNode[] parts = data.GetNodes();
            AvailablePart matched_part;

            alt_name = data.GetValue("ship");
            description = data.GetValue("description");
            construction_type = data.GetValue("type");
            if(!(construction_type == "SPH" || construction_type == "VAB")){
                construction_type = "Subassembly";
            }
            part_count = parts.Length;
            stage_count = 0;
            missing_parts = false;
            locked_parts = false;


            //interim variables used to collect values from GetPartCostsAndMass (defined outside of loop as a garbage reduction measure)
            float dry_mass = 0;
            float fuel_mass = 0;
            float dry_cost = 0;
            float fuel_cost = 0;
            string stage;


            foreach(ConfigNode part in parts){

                //Set the number of stages in the craft
                stage = part.GetValue("istg");
                if(!String.IsNullOrEmpty(stage)){
                    int stage_number = int.Parse(stage);
                    if(stage_number > stage_count){
                        stage_count = stage_number;
                    }
                }

                //locate part in game_parts and read part cost/mass information.
                matched_part = find_part(get_part_name(part));
                if(matched_part != null){
                    ShipConstruction.GetPartCostsAndMass(part, matched_part, out dry_cost, out fuel_cost, out dry_mass, out fuel_mass);
                    cost["dry"] += dry_cost;
                    cost["fuel"] += fuel_cost;
                    mass["dry"] += dry_mass;
                    mass["fuel"] += fuel_mass;
                    if(!ResearchAndDevelopment.PartTechAvailable(matched_part)){
                        locked_parts = true;
                    }

                } else{
                    missing_parts = true;
                }
            }

            stage_count += 1; //this might not be right
            cost["total"] = cost["dry"] + cost["fuel"];
            mass["total"] = mass["dry"] + mass["fuel"];
        }


        //get the part name from a PART config node.
        private string get_part_name(ConfigNode part){
            string part_name = part.GetValue("part");
            if(!String.IsNullOrEmpty(part_name)){
                part_name = part_name.Split('_')[0];
            } else{
                part_name = "";
            }
            return part_name;
        }


        private AvailablePart find_part(string part_name){
            if(game_parts.Count == 0){
                CraftManager.log("caching game parts");
                foreach(AvailablePart part in PartLoader.LoadedPartsList){
                    game_parts.Add(part.name, part);
                }
            }
            if(game_parts.ContainsKey(part_name)){
                return game_parts[part_name];
            }
            return null;
        }

        public List<string> tags(){
            return Tags.tags_for(Tags.craft_reference_key(this));
        }

    }

}

