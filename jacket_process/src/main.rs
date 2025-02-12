use image::{GenericImageView, Rgba, ImageBuffer, DynamicImage};
use imageproc::drawing::draw_hollow_rect_mut;
use imageproc::rect::Rect;
use clap::Parser;
//use std::time::Instant; // debug

fn get_border_color(img: &DynamicImage) -> Rgba<u8> {
    let img = img.to_rgba8();
    let (width, height) = img.dimensions();
    let border_thickness = 30;
    
    let mut total_r: u64 = 0;
    let mut total_g: u64 = 0;
    let mut total_b: u64 = 0;
    let mut pixel_count: u64 = 0;

    // Process top and bottom borders (including corners)
    for y in [0..border_thickness, height-border_thickness..height] {
        for x in 0..width {
            let pixel = img.get_pixel(x, y.start);
            total_r += pixel[0] as u64;
            total_g += pixel[1] as u64;
            total_b += pixel[2] as u64;
            pixel_count += 1;
        }
    }

    // Process left and right borders (excluding corners)
    for x in [0..border_thickness, width-border_thickness..width] {
        for y in border_thickness..height-border_thickness {
            let pixel = img.get_pixel(x.start, y);
            total_r += pixel[0] as u64;
            total_g += pixel[1] as u64;
            total_b += pixel[2] as u64;
            pixel_count += 1;
        }
    }

    Rgba([
        (total_r / pixel_count) as u8,
        (total_g / pixel_count) as u8,
        (total_b / pixel_count) as u8,
        255
    ])
}

fn draw_thick_border(
    img: &mut ImageBuffer<Rgba<u8>, Vec<u8>>,
    rect: Rect,
    color: Rgba<u8>,
    thickness: u32
) {
    for i in 0..thickness {
        draw_hollow_rect_mut(
            img,
            Rect::at(
                rect.left() + i as i32,
                rect.top() + i as i32
            ).of_size(
                rect.width() - 2 * i,
                rect.height() - 2 * i
            ),
            color,
        );
    }
}

#[derive(Parser)]
#[command(author, version, about, long_about = None)]
struct Args {
    #[arg(long, required = true)]
    size: u32,
    #[arg(long, required = true)]
    upscayl: u32,
    #[arg(long, default_value = "digital-art-4x")]
    model: String,
    #[arg(long, default_value = "1")]
    scale: u32,
}

fn process_image() {

    //let start = Instant::now(); // debug
    //println!("process start"); // debug

    // Check files and args
    let args = Args::parse();
    if args.size < 200 || args.size > 5000 { 
        eprintln!("--size must be between 200 and 5000");
        return; 
    }
    if args.upscayl != 0 && args.upscayl != 1 { 
        eprintln!("--upscayl must be 0 or 1");
        return; 
    }
    if args.scale < 1 || args.scale > 8 {
        eprintln!("--scale must be between 1 and 8");
        return;
    }

    let exe_path = std::env::current_exe().expect("Failed to get executable path");
    let parent = exe_path.parent().expect("Failed to get parent directory");
    let output_path = parent.join("output.png");

    let input_path = parent.join("input.png");
    let white_path = parent.join("white.png");
    let black_path = parent.join("black.png");
    if !input_path.exists() { return; }
    if !white_path.exists() { return; }
    if !black_path.exists() { return; }
    
    let upscayl_path = parent.join("upscayl-bin.exe");
    if args.upscayl == 1 {
        if !upscayl_path.exists() { return; }
    } // only check upscayl-bin.exe if upscayl is enabled

    // Load central image
    let central = if args.upscayl == 0 {
        // Resize to 1/2 of target size
        match image::open(&input_path) {
            Ok(img) => {
                let size = args.size / 2;
                img.resize(size, size, image::imageops::FilterType::Lanczos3)
            }
            Err(e) => {
                eprintln!("Failed to open input image: {}", e);
                return;
            }
        }
    } else {
        // Resize input as output.png
        let size = args.size / (2 * args.scale);
        match image::open(&input_path) {
            Ok(img) => {
                let resized = img.resize(size, size, image::imageops::FilterType::Lanczos3);
                if let Err(e) = resized.save(&output_path) {
                    eprintln!("Failed to save resized input image: {}", e);
                    return;
                }
            }
            Err(e) => {
                eprintln!("Failed to open input image: {}", e);
                return;
            }
        }

        // Run upscayl
        let status = match std::process::Command::new(&upscayl_path)
            .args([
                "-i", "output.png",
                "-o", "output.png",
                "-n", &args.model,
                "-s", &args.scale.to_string()
            ])
            .status() {
                Ok(s) => s,
                Err(e) => {
                    eprintln!("Failed to execute upscayl: {}", e);
                    return;
                }
            };

        if !status.success() {
            eprintln!("Upscayl process failed");
            return;
        }

        // Read upscaled image
        match image::open(&output_path) {
            Ok(img) => img,
            Err(e) => {
                eprintln!("Failed to open upscaled image: {}", e);
                return;
            }
        }
    };

    //let duration = start.elapsed(); // debug
    //println!("input.png processed {:.2?}", duration); // debug


    // Get blur background
    let (width, height) = central.dimensions();
    let background_s = central.fast_blur(100.0);
    let background = background_s.resize(width*2, height*2, image::imageops::FilterType::Nearest);
    let mut background = background.to_rgba8();


    // Get adjusted border color
    let border_color = get_border_color(&background_s);
    let grey = 0.3 * border_color[0] as f32 + 
               0.59 * border_color[1] as f32 + 
               0.11 * border_color[2] as f32;

    let border_color = if grey > 210.0 {
        // Darker 25%
        let i = 0.75f32;
        (
            (border_color[0] as f32 * i) as u8,
            (border_color[1] as f32 * i) as u8,
            (border_color[2] as f32 * i) as u8,
        )
    } else if grey < 50.0 {
        // Lighter 40%
        let i = f32::max(1.4, 60.0 / grey);
        (
            (border_color[0] as f32 * i) as u8,
            (border_color[1] as f32 * i) as u8,
            (border_color[2] as f32 * i) as u8,
        )
    } else {
        (border_color[0], border_color[1], border_color[2])
    };


    // Get frame
    let frame = match image::open(if grey > 128.0 { &black_path } else { &white_path }) {
        Ok(img) => {
            let new_size = (1.25 * width as f32) as u32;
            img.resize(new_size, new_size, image::imageops::FilterType::Lanczos3)
        }
        Err(e) => {
            eprintln!("Failed to open black or white: {}", e);
            return;
        }
    };


    // Paste central to background
    let x = width / 2;
    let frame_offset = width as f32 * 0.375;
    image::imageops::replace(&mut background, &central.to_rgba8(), x as i64, x as i64);
    image::imageops::overlay(&mut background, &frame.to_rgba8(), frame_offset as i64, frame_offset as i64);

    // Draw border
    let bwidth = (x - 50) / 100 + 1;
    let x1 = (width as f32 * 0.46) as u32;
    let x2 = (width as f32 * 1.08) as u32;
    let dist = x / 21;

    draw_thick_border(
        &mut background,
        Rect::at(x1 as i32, x1 as i32)
            .of_size(x2, x2),
        Rgba([border_color.0, border_color.1, border_color.2, 255]),
        bwidth
    );

    draw_thick_border(
        &mut background,
        Rect::at((x1 - dist) as i32, (x1 - dist) as i32)
            .of_size(x2 + 2*dist, x2 + 2*dist),
        Rgba([border_color.0, border_color.1, border_color.2, 255]),
        bwidth
    );

    // Save result
    match background.save(&output_path) {
        Ok(_) => println!("Success"),
        Err(e) => eprintln!("Failed to save image: {}", e),
    }

    //let duration = start.elapsed(); // debug
    //println!("final {:.2?}", duration); // debug
    //std::thread::sleep(std::time::Duration::from_secs(3)); // debug
}

fn main() {
    process_image();
}
